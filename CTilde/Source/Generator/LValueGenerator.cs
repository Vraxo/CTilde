using System;

namespace CTilde.Generator;

public class LValueGenerator
{
    private readonly CodeGenerator _codeGenerator;
    private AssemblyBuilder Builder => _codeGenerator.Builder;
    private TypeRepository TypeRepository => _codeGenerator.TypeRepository;
    private MemoryLayoutManager MemoryLayoutManager => _codeGenerator.MemoryLayoutManager;
    private SemanticAnalyzer SemanticAnalyzer => _codeGenerator.SemanticAnalyzer;

    public LValueGenerator(CodeGenerator codeGenerator)
    {
        _codeGenerator = codeGenerator;
    }

    public void GenerateLValueAddress(ExpressionNode expression, AnalysisContext context)
    {
        switch (expression)
        {
            case VariableExpressionNode varExpr: GenerateLValueForVariable(varExpr, context); break;
            case MemberAccessExpressionNode memberAccess: GenerateLValueForMemberAccess(memberAccess, context); break;
            case UnaryExpressionNode u when u.Operator.Type == TokenType.Star:
                // Address of a dereference is just the value of the pointer expression
                _codeGenerator.ExpressionGenerator.GenerateExpression(u.Right, context);
                break;
            default: throw new InvalidOperationException($"Expression '{expression.GetType().Name}' is not a valid L-value.");
        }
    }

    private void GenerateLValueForVariable(VariableExpressionNode varExpr, AnalysisContext context)
    {
        if (context.Symbols.TryGetSymbol(varExpr.Identifier.Value, out var offset, out _, out _))
        {
            string sign = offset > 0 ? "+" : "";
            Builder.AppendInstruction($"lea eax, [ebp {sign} {offset}]", $"Get address of var/param {varExpr.Identifier.Value}");
            return;
        }

        if (context.CurrentFunction?.OwnerStructName != null)
        {
            try
            {
                string ownerStructFqn = TypeRepository.GetFullyQualifiedOwnerName(context.CurrentFunction)!;

                var (memberOffset, _) = MemoryLayoutManager.GetMemberInfo(ownerStructFqn, varExpr.Identifier.Value, context.CompilationUnit);
                context.Symbols.TryGetSymbol("this", out var thisOffset, out _, out _);
                Builder.AppendInstruction($"mov eax, [ebp + {thisOffset}]", "Get `this` pointer value");
                if (memberOffset > 0) Builder.AppendInstruction($"add eax, {memberOffset}", $"Offset for implicit this->{varExpr.Identifier.Value}");
                return;
            }
            catch (InvalidOperationException) { /* Fall through */ }
        }
        throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'.");
    }

    private void GenerateLValueForMemberAccess(MemberAccessExpressionNode memberAccess, AnalysisContext context)
    {
        var leftType = SemanticAnalyzer.AnalyzeExpressionType(memberAccess.Left, context);
        string baseStructType = leftType.TrimEnd('*');
        var (memberOffset, _) = MemoryLayoutManager.GetMemberInfo(baseStructType, memberAccess.Member.Value, context.CompilationUnit);

        // For `ptr->member`, we generate the expression for the pointer first.
        if (memberAccess.Operator.Type == TokenType.Arrow)
        {
            _codeGenerator.ExpressionGenerator.GenerateExpression(memberAccess.Left, context);
        }
        // For `obj.member`, we get the address of the object first.
        else
        {
            GenerateLValueAddress(memberAccess.Left, context);
        }

        if (memberOffset > 0) Builder.AppendInstruction($"add eax, {memberOffset}", $"Offset for member {memberAccess.Operator.Value}{memberAccess.Member.Value}");
    }
}