using System;
using System.Linq;

namespace CTilde;

public class StatementGenerator
{
    private readonly CodeGenerator _context;
    private AssemblyBuilder Builder => _context.Builder;
    private TypeManager TypeManager => _context.TypeManager;
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
            case ExpressionStatementNode exprStmt: ExpressionGenerator.GenerateExpression(exprStmt.Expression, context); break;
            default: throw new NotImplementedException($"Stmt: {statement.GetType().Name}");
        }
    }

    private void GenerateDelete(DeleteStatementNode deleteNode, AnalysisContext context)
    {
        ExpressionGenerator.GenerateExpression(deleteNode.Expression, context);
        Builder.AppendInstruction("mov edi, eax", "Save pointer to be deleted in edi");
        Builder.AppendInstruction("push edi", "Push 'this' pointer for destructor call");

        var pointerType = _context.SemanticAnalyzer.AnalyzeExpressionType(deleteNode.Expression, context);
        var objectType = pointerType.TrimEnd('*');
        var dtor = TypeManager.FindDestructor(objectType);

        if (dtor != null)
        {
            if (dtor.IsVirtual)
            {
                Builder.AppendInstruction("mov eax, [edi]", "Get vtable pointer from object");
                Builder.AppendInstruction("mov eax, [eax]", "Get destructor from vtable[0]");
                Builder.AppendInstruction("call eax");
            }
            else
            {
                Builder.AppendInstruction($"call {TypeManager.Mangle(dtor)}");
            }
        }

        Builder.AppendInstruction("add esp, 4", "Clean up 'this' from dtor call");
        Builder.AppendInstruction("push edi", "Push pointer for free()");
        Builder.AppendInstruction("call [free]");
        Builder.AppendInstruction("add esp, 4", "Clean up pointer from free() call");
    }

    private void GenerateDeclaration(DeclarationStatementNode decl, AnalysisContext context)
    {
        var variableName = decl.Identifier.Value;
        var varTypeFqn = context.Symbols.GetSymbolType(variableName);
        context.Symbols.TryGetSymbol(variableName, out var offset, out _, out _);

        if (TypeManager.IsStruct(varTypeFqn))
        {
            var structDef = TypeManager.FindStruct(varTypeFqn);
            if (TypeManager.HasVTable(varTypeFqn))
            {
                var vtableLabel = TypeManager.GetVTableLabel(structDef);
                Builder.AppendInstruction($"lea eax, [ebp + {offset}]", $"Get address of object '{variableName}'");
                Builder.AppendInstruction($"mov dword [eax], {vtableLabel}", "Set vtable pointer");
            }
        }

        if (decl.ConstructorArguments != null)
        {
            int argCount = decl.ConstructorArguments.Count;
            var ctor = TypeManager.FindConstructor(varTypeFqn, argCount) ?? throw new InvalidOperationException($"No constructor found for '{varTypeFqn}' with {argCount} arguments.");

            int totalArgSize = 0;
            foreach (var arg in decl.ConstructorArguments.AsEnumerable().Reverse())
            {
                totalArgSize += ExpressionGenerator.PushArgument(arg, context);
            }

            Builder.AppendInstruction($"lea eax, [ebp + {offset}]", $"Push 'this' for constructor");
            Builder.AppendInstruction("push eax");
            totalArgSize += 4;

            Builder.AppendInstruction($"call {TypeManager.Mangle(ctor)}");
            Builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up ctor args");
        }
        else if (decl.Initializer is InitializerListExpressionNode initList)
        {
            var allMembers = TypeManager.GetAllMembers(varTypeFqn, context.CompilationUnit);
            if (initList.Values.Count > allMembers.Count) throw new InvalidOperationException($"Too many values in initializer list for struct '{varTypeFqn}'.");

            for (int j = 0; j < initList.Values.Count; j++)
            {
                var (memberName, memberType, memberOffset, _) = allMembers[j];
                var valueExpr = initList.Values[j];
                var memberSize = TypeManager.GetSizeOfType(memberType, context.CompilationUnit);
                var totalOffset = offset + memberOffset;

                ExpressionGenerator.GenerateExpression(valueExpr, context);
                if (memberSize == 1) Builder.AppendInstruction($"mov byte [ebp + {totalOffset}], al", $"Init member {memberName}");
                else Builder.AppendInstruction($"mov dword [ebp + {totalOffset}], eax", $"Init member {memberName}");
            }
        }
        else if (decl.Initializer != null)
        {
            ExpressionGenerator.GenerateExpression(decl.Initializer, context);
            if (TypeManager.GetSizeOfType(varTypeFqn, context.CompilationUnit) == 1)
                Builder.AppendInstruction($"mov byte [ebp + {offset}], al", $"Initialize {variableName}");
            else
                Builder.AppendInstruction($"mov dword [ebp + {offset}], eax", $"Initialize {variableName}");
        }
    }

    private void GenerateReturn(ReturnStatementNode ret, AnalysisContext context)
    {
        if (ret.Expression != null) ExpressionGenerator.GenerateExpression(ret.Expression, context);
        // NOTE: The actual return instruction is now handled by the epilogue generation in CodeGenerator
        // to ensure destructors are called first.
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
        Builder.AppendInstruction(i.ElseBody != null ? $"je _if_else_{idx}" : $"je _if_end_{idx}");
        GenerateStatement(i.ThenBody, context);
        if (i.ElseBody != null)
        {
            Builder.AppendInstruction($"jmp _if_end_{idx}");
            Builder.AppendLabel($"_if_else_{idx}");
            GenerateStatement(i.ElseBody, context);
        }
        Builder.AppendLabel($"_if_end_{idx}");
    }
}