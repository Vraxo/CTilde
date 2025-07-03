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
            case DeclarationStatementNode decl:
                GenerateDeclaration(decl, context);
                break;
            case ExpressionStatementNode exprStmt: ExpressionGenerator.GenerateExpression(exprStmt.Expression, context); break;
            default: throw new NotImplementedException($"Stmt: {statement.GetType().Name}");
        }
    }

    private void GenerateDeclaration(DeclarationStatementNode decl, AnalysisContext context)
    {
        var variableName = decl.Identifier.Value;
        var varTypeFqn = context.Symbols.GetSymbolType(variableName);
        context.Symbols.TryGetSymbol(variableName, out var offset, out _, out _);

        // If it's a struct with a vtable, initialize its vptr
        if (TypeManager.IsStruct(varTypeFqn) && TypeManager.HasVTable(varTypeFqn))
        {
            var structDef = TypeManager.FindStruct(varTypeFqn);
            var mangledStructName = TypeManager.Mangle(structDef);
            Builder.AppendInstruction($"lea eax, [ebp + {offset}]", $"Get address of object '{variableName}'");
            Builder.AppendInstruction($"mov dword [eax], _vtable_{mangledStructName}", "Set vtable pointer");
        }

        if (decl.Initializer == null) return;

        if (decl.Initializer is InitializerListExpressionNode initList)
        {
            var structDef = TypeManager.FindStruct(varTypeFqn)
                ?? throw new InvalidOperationException($"Could not find struct definition for initializer list type '{varTypeFqn}'.");

            if (initList.Values.Count > structDef.Members.Count)
                throw new InvalidOperationException($"Too many values in initializer list for struct '{structDef.Name}'.");

            var allMembers = TypeManager.GetAllMembers(varTypeFqn, context.CompilationUnit);

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
        else // Simple variable initialization (e.g., int x = 5; or const int x = 5;)
        {
            ExpressionGenerator.GenerateExpression(decl.Initializer, context); // Value to assign is in EAX

            if (TypeManager.GetSizeOfType(varTypeFqn, context.CompilationUnit) == 1)
            {
                Builder.AppendInstruction($"mov byte [ebp + {offset}], al", $"Initialize {variableName}");
            }
            else
            {
                Builder.AppendInstruction($"mov dword [ebp + {offset}], eax", $"Initialize {variableName}");
            }
        }
    }

    private void GenerateReturn(ReturnStatementNode ret, AnalysisContext context)
    {
        if (ret.Expression != null) ExpressionGenerator.GenerateExpression(ret.Expression, context);
        _context.GenerateFunctionEpilogue();
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