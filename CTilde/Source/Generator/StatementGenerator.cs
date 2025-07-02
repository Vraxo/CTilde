using System;
using System.Linq;

namespace CTilde;

public class StatementGenerator
{
    private readonly CodeGenerator _context;
    private AssemblyBuilder Builder => _context.Builder;
    private TypeManager TypeManager => _context.TypeManager;
    private CompilationUnitNode CurrentCompilationUnit => _context.CurrentCompilationUnit;
    private FunctionDeclarationNode CurrentFunction => _context.CurrentFunction;
    private ExpressionGenerator ExpressionGenerator => _context.ExpressionGenerator;

    public StatementGenerator(CodeGenerator context)
    {
        _context = context;
    }

    public void GenerateStatement(StatementNode statement)
    {
        switch (statement)
        {
            case ReturnStatementNode ret: GenerateReturn(ret); break;
            case BlockStatementNode block: foreach (var s in block.Statements) GenerateStatement(s); break;
            case WhileStatementNode w: GenerateWhile(w); break;
            case IfStatementNode i: GenerateIf(i); break;
            case DeclarationStatementNode decl:
                if (decl.Initializer != null)
                {
                    if (decl.Initializer is InitializerListExpressionNode initList)
                    {
                        string rawTypeName = TypeManager.GetTypeName(decl.Type, decl.PointerLevel);

                        // Resolve the base type name, then append the pointer suffix back if any
                        string baseTypeName = rawTypeName.TrimEnd('*');
                        string pointerSuffix = new string('*', rawTypeName.Length - baseTypeName.Length);
                        string resolvedTypeName = TypeManager.ResolveTypeName(baseTypeName, CurrentFunction.Namespace, CurrentCompilationUnit) + pointerSuffix;

                        if (!TypeManager.IsStruct(resolvedTypeName))
                            throw new InvalidOperationException($"Initializer list can only be used for struct types, not '{rawTypeName}'.");

                        var structDef = TypeManager.FindStruct(resolvedTypeName)
                            ?? throw new InvalidOperationException($"Could not find struct definition for initializer list type '{resolvedTypeName}'.");

                        if (initList.Values.Count > structDef.Members.Count)
                            throw new InvalidOperationException($"Too many values in initializer list for struct '{structDef.Name}'.");

                        _context.CurrentSymbols.TryGetSymbol(decl.Identifier.Value, out var structBaseOffset, out _, out _); // Added out _
                        int currentMemberOffset = 0;

                        for (int j = 0; j < initList.Values.Count; j++)
                        {
                            var member = structDef.Members[j];
                            var valueExpr = initList.Values[j];

                            // Resolve the member type name before getting its size
                            var rawMemberTypeName = TypeManager.GetTypeName(member.Type, member.PointerLevel);
                            var baseMemberName = rawMemberTypeName.TrimEnd('*');
                            var memberPointerSuffix = new string('*', rawMemberTypeName.Length - baseMemberName.Length);

                            string resolvedMemberTypeName;
                            if (member.Type.Type == TokenType.Keyword || baseMemberName.Equals("void", StringComparison.OrdinalIgnoreCase))
                            {
                                resolvedMemberTypeName = rawMemberTypeName; // Primitive types, void don't need resolution
                            }
                            else
                            {
                                // Resolve member type using the namespace of the *struct definition*, not the current function's context
                                resolvedMemberTypeName = TypeManager.ResolveTypeName(baseMemberName, structDef.Namespace, CurrentCompilationUnit) + memberPointerSuffix;
                            }

                            var memberSize = TypeManager.GetSizeOfType(resolvedMemberTypeName, CurrentCompilationUnit);
                            var totalOffset = structBaseOffset + currentMemberOffset;

                            ExpressionGenerator.GenerateExpression(valueExpr);
                            if (memberSize == 1) Builder.AppendInstruction($"mov byte [ebp + {totalOffset}], al", $"Init member {member.Name.Value}");
                            else Builder.AppendInstruction($"mov dword [ebp + {totalOffset}], eax", $"Init member {member.Name.Value}");

                            currentMemberOffset += memberSize;
                        }
                    }
                    else // Simple variable initialization (e.g., int x = 5; or const int x = 5;)
                    {
                        var variableName = decl.Identifier.Value;
                        var varType = _context.CurrentSymbols.GetSymbolType(variableName); // Get FQN type from symbol table

                        ExpressionGenerator.GenerateExpression(decl.Initializer); // Value to assign is in EAX
                        _context.CurrentSymbols.TryGetSymbol(variableName, out var offset, out _, out _); // Added out _

                        if (TypeManager.GetSizeOfType(varType, CurrentCompilationUnit) == 1)
                        {
                            Builder.AppendInstruction($"mov byte [ebp + {offset}], al", $"Initialize {variableName}");
                        }
                        else
                        {
                            Builder.AppendInstruction($"mov dword [ebp + {offset}], eax", $"Initialize {variableName}");
                        }
                    }
                }
                break;
            case ExpressionStatementNode exprStmt: ExpressionGenerator.GenerateExpression(exprStmt.Expression); break;
            default: throw new NotImplementedException($"Stmt: {statement.GetType().Name}");
        }
    }

    private void GenerateReturn(ReturnStatementNode ret)
    {
        if (ret.Expression != null) ExpressionGenerator.GenerateExpression(ret.Expression);
        _context.GenerateFunctionEpilogue();
    }

    private void GenerateWhile(WhileStatementNode w)
    {
        int i = _context.GetNextLabelId();
        Builder.AppendLabel($"_while_start_{i}");
        ExpressionGenerator.GenerateExpression(w.Condition);
        Builder.AppendInstruction("cmp eax, 0");
        Builder.AppendInstruction($"je _while_end_{i}");
        GenerateStatement(w.Body);
        Builder.AppendInstruction($"jmp _while_start_{i}");
        Builder.AppendLabel($"_while_end_{i}");
    }

    private void GenerateIf(IfStatementNode i)
    {
        int idx = _context.GetNextLabelId();
        ExpressionGenerator.GenerateExpression(i.Condition);
        Builder.AppendInstruction("cmp eax, 0");
        Builder.AppendInstruction(i.ElseBody != null ? $"je _if_else_{idx}" : $"je _if_end_{idx}");
        GenerateStatement(i.ThenBody);
        if (i.ElseBody != null)
        {
            Builder.AppendInstruction($"jmp _if_end_{idx}");
            Builder.AppendLabel($"_if_else_{idx}");
            GenerateStatement(i.ElseBody);
        }
        Builder.AppendLabel($"_if_end_{idx}");
    }
}