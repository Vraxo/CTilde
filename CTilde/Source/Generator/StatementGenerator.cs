using System;
using System.Linq;

namespace CTilde;

public class StatementGenerator
{
    private readonly CodeGenerator _context;
    private AssemblyBuilder Builder => _context.Builder;
    private TypeManager TypeManager => _context.TypeManager;
    private ProgramNode Program => _context.Program;
    private SymbolTable CurrentSymbols => _context.CurrentSymbols;
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
                        string typeName = TypeManager.GetTypeName(decl.Type, decl.PointerLevel);
                        if (!TypeManager.IsStruct(typeName))
                            throw new InvalidOperationException($"Initializer list can only be used for struct types, not '{typeName}'.");

                        var structDef = TypeManager.FindStruct(typeName);
                        if (structDef == null)
                            throw new InvalidOperationException($"Could not find struct definition for initializer list type '{typeName}'.");


                        if (initList.Values.Count > structDef.Members.Count)
                            throw new InvalidOperationException($"Too many values in initializer list for struct '{structDef.Name}'.");

                        CurrentSymbols.TryGetSymbol(decl.Identifier.Value, out var structBaseOffset, out _);
                        int currentMemberOffset = 0;

                        for (int j = 0; j < initList.Values.Count; j++)
                        {
                            var member = structDef.Members[j];
                            var valueExpr = initList.Values[j];

                            var memberTypeName = TypeManager.GetTypeName(member.Type, member.PointerLevel);
                            var memberSize = TypeManager.GetSizeOfType(memberTypeName);
                            var totalOffset = structBaseOffset + currentMemberOffset;

                            ExpressionGenerator.GenerateExpression(valueExpr);
                            if (memberSize == 1) Builder.AppendInstruction($"mov byte [ebp + {totalOffset}], al", $"Init member {member.Name.Value}");
                            else Builder.AppendInstruction($"mov dword [ebp + {totalOffset}], eax", $"Init member {member.Name.Value}");

                            currentMemberOffset += memberSize;
                        }
                    }
                    else
                    {
                        var left = new VariableExpressionNode(decl.Identifier);
                        var assignment = new AssignmentExpressionNode(left, decl.Initializer);
                        ExpressionGenerator.GenerateExpression(assignment);
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