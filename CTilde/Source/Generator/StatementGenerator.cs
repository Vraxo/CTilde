﻿using System;
using System.Linq;

namespace CTilde;

public class StatementGenerator
{
    private readonly CodeGenerator _context;
    private AssemblyBuilder Builder => _context.Builder;
    private TypeRepository TypeRepository => _context.TypeRepository;
    private FunctionResolver FunctionResolver => _context.FunctionResolver;
    private MemoryLayoutManager MemoryLayoutManager => _context.MemoryLayoutManager;
    private ExpressionGenerator ExpressionGenerator => _context.ExpressionGenerator;

    public StatementGenerator(CodeGenerator context)
    {
        _context = context;
    }

    public void GenerateStatement(StatementNode statement, AnalysisContext context)
    {
        switch (statement)
        {
            case ReturnStatementNode ret: GenerateReturn(ret, context); break;
            case BlockStatementNode block: foreach (var s in block.Statements) GenerateStatement(s, context); break;
            case WhileStatementNode w: GenerateWhile(w, context); break;
            case IfStatementNode i: GenerateIf(i, context); break;
            case DeleteStatementNode d: GenerateDelete(d, context); break;
            case DeclarationStatementNode decl:
                GenerateDeclaration(decl, context);
                break;
            case ExpressionStatementNode exprStmt:
                ExpressionGenerator.GenerateExpression(exprStmt.Expression, context);
                // Check if the expression statement was a function call that returned a temporary struct
                var exprType = _context.SemanticAnalyzer.AnalyzeExpressionType(exprStmt.Expression, context);
                if (_context.TypeRepository.IsStruct(exprType) && !exprType.EndsWith("*"))
                {
                    if (exprStmt.Expression is CallExpressionNode or BinaryExpressionNode)
                    {
                        // The temporary is on the stack but its value is unused. We must destroy it.
                        var tempDtor = FunctionResolver.FindDestructor(exprType);
                        if (tempDtor is not null)
                        {
                            Builder.AppendInstruction(null, "Destroying temporary from expression statement");
                            Builder.AppendInstruction("lea eax, [esp]"); // Address of temporary is at ESP
                            Builder.AppendInstruction("push eax"); // Push 'this'
                            if (tempDtor.IsVirtual)
                            {
                                Builder.AppendInstruction("mov eax, [eax]", "Get vtable ptr");
                                Builder.AppendInstruction("mov eax, [eax]", "Get dtor from vtable[0]");
                                Builder.AppendInstruction("call eax");
                            }
                            else
                            {
                                Builder.AppendInstruction($"call {NameMangler.Mangle(tempDtor)}");
                            }
                            Builder.AppendInstruction("add esp, 4"); // Clean up 'this'
                        }
                        var size = MemoryLayoutManager.GetSizeOfType(exprType, context.CompilationUnit);
                        Builder.AppendInstruction($"add esp, {size}", "Clean up temporary return object from stack");
                    }
                }
                break;
            default: throw new NotImplementedException($"Stmt: {statement.GetType().Name}");
        }
    }

    private void GenerateDelete(DeleteStatementNode deleteNode, AnalysisContext context)
    {
        ExpressionGenerator.GenerateExpression(deleteNode.Expression, context);
        Builder.AppendInstruction("mov edi, eax", "Save pointer to be deleted in edi");

        var pointerType = _context.SemanticAnalyzer.AnalyzeExpressionType(deleteNode.Expression, context);
        var objectType = pointerType.TrimEnd('*');

        // If the object's type has a vtable, its destructor MUST be called virtually.
        // Otherwise, call it directly if it exists.
        if (_context.VTableManager.HasVTable(objectType))
        {
            Builder.AppendInstruction("push edi", "Push 'this' pointer for virtual dtor call");
            Builder.AppendInstruction("mov eax, [edi]", "Get vtable pointer from object");
            Builder.AppendInstruction("mov eax, [eax]", "Get destructor from vtable[0]");
            Builder.AppendInstruction("call eax");
            Builder.AppendInstruction("add esp, 4", "Clean up 'this' from dtor call");
        }
        else
        {
            var dtor = FunctionResolver.FindDestructor(objectType);
            if (dtor is not null)
            {
                Builder.AppendInstruction("push edi", "Push 'this' pointer for non-virtual dtor call");
                Builder.AppendInstruction($"call {NameMangler.Mangle(dtor)}");
                Builder.AppendInstruction("add esp, 4", "Clean up 'this' from dtor call");
            }
        }

        Builder.AppendInstruction("push edi", "Push pointer for free()");
        Builder.AppendInstruction("call [free]");
        Builder.AppendInstruction("add esp, 4", "Clean up pointer from free() call");
    }

    private void GenerateDeclaration(DeclarationStatementNode decl, AnalysisContext context)
    {
        var variableName = decl.Identifier.Value;
        var varTypeFqn = context.Symbols.GetSymbolType(variableName);
        context.Symbols.TryGetSymbol(variableName, out var offset, out _, out _);

        if (TypeRepository.IsStruct(varTypeFqn))
        {
            if (_context.VTableManager.HasVTable(varTypeFqn))
            {
                var vtableLabel = NameMangler.GetVTableLabel(varTypeFqn);
                Builder.AppendInstruction($"lea eax, [ebp + {offset}]", $"Get address of object '{variableName}'");
                Builder.AppendInstruction($"mov dword [eax], {vtableLabel}", "Set vtable pointer");
            }

            if (decl.ConstructorArguments is not null) // e.g. string s("hello");
            {
                var argTypes = decl.ConstructorArguments
                    .Select(arg => _context.SemanticAnalyzer.AnalyzeExpressionType(arg, context))
                    .ToList();
                var ctor = FunctionResolver.FindConstructor(varTypeFqn, argTypes) ?? throw new InvalidOperationException($"No constructor found for '{varTypeFqn}' matching signature.");

                int totalArgSize = 0;
                foreach (var arg in decl.ConstructorArguments.AsEnumerable().Reverse())
                {
                    totalArgSize += ExpressionGenerator.PushArgument(arg, context);
                }

                Builder.AppendInstruction($"lea eax, [ebp + {offset}]", $"Push 'this' for constructor");
                Builder.AppendInstruction("push eax");
                totalArgSize += 4;

                Builder.AppendInstruction($"call {NameMangler.Mangle(ctor)}");
                Builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up ctor args");
            }
            else if (decl.Initializer is not null)
            {
                if (decl.Initializer is InitializerListExpressionNode initList) // e.g. MyStruct s = {1, 2};
                {
                    var allMembers = MemoryLayoutManager.GetAllMembers(varTypeFqn, context.CompilationUnit);
                    if (initList.Values.Count > allMembers.Count) throw new InvalidOperationException($"Too many values in initializer list for struct '{varTypeFqn}'.");

                    for (int j = 0; j < initList.Values.Count; j++)
                    {
                        var (memberName, memberType, memberOffset, _) = allMembers[j];
                        var valueExpr = initList.Values[j];
                        var memberSize = MemoryLayoutManager.GetSizeOfType(memberType, context.CompilationUnit);
                        var totalOffset = offset + memberOffset;

                        ExpressionGenerator.GenerateExpression(valueExpr, context);
                        if (memberSize == 1) Builder.AppendInstruction($"mov byte [ebp + {totalOffset}], al", $"Init member {memberName}");
                        else Builder.AppendInstruction($"mov dword [ebp + {totalOffset}], eax", $"Init member {memberName}");
                    }
                }
                else // Covers implicit constructor calls AND pointer initialization
                {
                    string initializerType = _context.SemanticAnalyzer.AnalyzeExpressionType(decl.Initializer, context);

                    if (varTypeFqn.EndsWith("*"))
                    {
                        // Pointer initialization, not a constructor call.
                        ExpressionGenerator.GenerateExpression(decl.Initializer, context);
                        Builder.AppendInstruction($"mov dword [ebp + {offset}], eax", $"Initialize pointer {variableName}");
                    }
                    else
                    {
                        // Implicit constructor call for a value-type struct.
                        var ctor = FunctionResolver.FindConstructor(varTypeFqn, new List<string> { initializerType });

                        bool takeAddressOfInitializer = false;
                        if (ctor is null && TypeRepository.IsStruct(initializerType))
                        {
                            // Try to find a copy constructor that takes a pointer, e.g. string(string*)
                            ctor = FunctionResolver.FindConstructor(varTypeFqn, new List<string> { initializerType + "*" });
                            if (ctor is not null)
                            {
                                takeAddressOfInitializer = true;
                            }
                        }

                        if (ctor is null)
                        {
                            throw new InvalidOperationException($"No constructor found for '{varTypeFqn}' that takes an argument of type '{initializerType}'.");
                        }

                        int totalArgSize;
                        if (takeAddressOfInitializer)
                        {
                            ExpressionGenerator.GenerateExpression(decl.Initializer, context);
                            Builder.AppendInstruction("push eax", "Push pointer to initializer object for copy ctor");
                            totalArgSize = 4;
                        }
                        else
                        {
                            totalArgSize = ExpressionGenerator.PushArgument(decl.Initializer, context);
                        }

                        Builder.AppendInstruction($"lea eax, [ebp + {offset}]", $"Push 'this' for constructor");
                        Builder.AppendInstruction("push eax");
                        totalArgSize += 4;

                        Builder.AppendInstruction($"call {NameMangler.Mangle(ctor)}");
                        Builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up ctor args");

                        if (decl.Initializer is CallExpressionNode or BinaryExpressionNode &&
                            _context.TypeRepository.IsStruct(initializerType) && !initializerType.EndsWith("*"))
                        {
                            var tempDtor = FunctionResolver.FindDestructor(initializerType);
                            if (tempDtor is not null)
                            {
                                Builder.AppendInstruction(null, "Destroying temporary from initialization");
                                Builder.AppendInstruction("lea eax, [esp]");
                                Builder.AppendInstruction("push eax");
                                if (tempDtor.IsVirtual)
                                {
                                    Builder.AppendInstruction("mov eax, [eax]");
                                    Builder.AppendInstruction("mov eax, [eax]");
                                    Builder.AppendInstruction("call eax");
                                }
                                else
                                {
                                    Builder.AppendInstruction($"call {NameMangler.Mangle(tempDtor)}");
                                }
                                Builder.AppendInstruction("add esp, 4");
                            }
                            var size = MemoryLayoutManager.GetSizeOfType(initializerType, context.CompilationUnit);
                            Builder.AppendInstruction($"add esp, {size}", "Clean up temporary return object from stack");
                        }
                    }
                }
            }
            else // Default constructor call, e.g. List textObjects;
            {
                var ctor = FunctionResolver.FindConstructor(varTypeFqn, new List<string>());
                if (ctor is not null)
                {
                    Builder.AppendInstruction(null, $"Calling default constructor for {variableName}");
                    Builder.AppendInstruction($"lea eax, [ebp + {offset}]", $"Push 'this' for constructor");
                    Builder.AppendInstruction("push eax");
                    Builder.AppendInstruction($"call {NameMangler.Mangle(ctor)}");
                    Builder.AppendInstruction("add esp, 4", "Clean up ctor args");
                }
            }
        }
        else if (decl.Initializer is not null) // Primitive types
        {
            ExpressionGenerator.GenerateExpression(decl.Initializer, context);
            if (MemoryLayoutManager.GetSizeOfType(varTypeFqn, context.CompilationUnit) == 1)
                Builder.AppendInstruction($"mov byte [ebp + {offset}], al", $"Initialize {variableName}");
            else
                Builder.AppendInstruction($"mov dword [ebp + {offset}], eax", $"Initialize {variableName}");
        }
    }

    private void GenerateReturn(ReturnStatementNode ret, AnalysisContext context)
    {
        var returnTypeFqn = _context.SemanticAnalyzer.GetFunctionReturnType(context.CurrentFunction, context);
        if (TypeRepository.IsStruct(returnTypeFqn) && !returnTypeFqn.EndsWith("*"))
        {
            if (ret.Expression is null) throw new InvalidOperationException("Must return a value from a function with a struct return type.");

            // Get the address of the local object being returned (e.g. &result)
            ExpressionGenerator.GenerateExpression(ret.Expression, context);
            Builder.AppendInstruction("mov esi, eax", "Source address for return value");

            // Get the hidden pointer to the destination (passed by the caller)
            context.Symbols.TryGetSymbol("__ret_ptr", out var retPtrOffset, out _, out _);
            Builder.AppendInstruction($"mov edi, [ebp + {retPtrOffset}]", "Destination address for return value");

            // Find a copy constructor, e.g. string(string*)
            var copyCtor = FunctionResolver.FindConstructor(returnTypeFqn, new List<string> { returnTypeFqn + "*" });

            if (copyCtor is not null)
            {
                Builder.AppendInstruction(null, "Calling copy constructor for return value");
                Builder.AppendInstruction("push esi", "Push source pointer argument");
                Builder.AppendInstruction("push edi", "Push destination pointer as 'this'");
                Builder.AppendInstruction($"call {NameMangler.Mangle(copyCtor)}");
                Builder.AppendInstruction("add esp, 8", "Clean up copy ctor args");
            }
            else
            {
                // Fallback to memcpy for POD structs without a copy constructor
                var size = MemoryLayoutManager.GetSizeOfType(returnTypeFqn, context.CompilationUnit);
                Builder.AppendInstruction($"push {size}");
                Builder.AppendInstruction("push esi");
                Builder.AppendInstruction("push edi");
                Builder.AppendInstruction("call [memcpy]");
                Builder.AppendInstruction("add esp, 12");
            }
        }
        else
        {
            // Handle return for primitive types
            if (ret.Expression is not null) ExpressionGenerator.GenerateExpression(ret.Expression, context);
        }

        // The actual `ret` instruction is in the epilogue to ensure destructors are called.
    }

    private void GenerateWhile(WhileStatementNode w, AnalysisContext context)
    {
        int i = _context.GetNextLabelId();
        Builder.AppendLabel($"_while_start_{i}");
        ExpressionGenerator.GenerateExpression(w.Condition, context);
        Builder.AppendInstruction("cmp eax, 0");
        Builder.AppendInstruction($"je _while_end_{i}");
        GenerateStatement(w.Body, context);
        Builder.AppendInstruction($"jmp _while_start_{i}");
        Builder.AppendLabel($"_while_end_{i}");
    }

    private void GenerateIf(IfStatementNode i, AnalysisContext context)
    {
        int idx = _context.GetNextLabelId();
        ExpressionGenerator.GenerateExpression(i.Condition, context);
        Builder.AppendInstruction("cmp eax, 0");
        Builder.AppendInstruction(i.ElseBody is not null ? $"je _if_else_{idx}" : $"je _if_end_{idx}");
        GenerateStatement(i.ThenBody, context);
        if (i.ElseBody is not null)
        {
            Builder.AppendInstruction($"jmp _if_end_{idx}");
            Builder.AppendLabel($"_if_else_{idx}");
            GenerateStatement(i.ElseBody, context);
        }
        Builder.AppendLabel($"_if_end_{idx}");
    }
}