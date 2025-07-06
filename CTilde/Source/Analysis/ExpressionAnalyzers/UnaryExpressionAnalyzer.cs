using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public class UnaryExpressionAnalyzer : ExpressionAnalyzerBase
{
    public UnaryExpressionAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var u = (UnaryExpressionNode)expr;

        return u.Operator.Type switch
        {
            TokenType.Ampersand => AnalyzeAddressOfOperator(u, context, diagnostics),
            TokenType.Star => AnalyzeDereferenceOperator(u, context, diagnostics),
            _ => _semanticAnalyzer.AnalyzeExpressionType(u.Right, context, diagnostics),
        };
    }

    private string AnalyzeAddressOfOperator(UnaryExpressionNode u, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        string operandType = _semanticAnalyzer.AnalyzeExpressionType(u.Right, context, diagnostics);
        
        if (operandType == "unknown")
        {
            return "unknown";
        }

        return operandType + "*";
    }

    private string AnalyzeDereferenceOperator(UnaryExpressionNode u, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        string operandType = _semanticAnalyzer.AnalyzeExpressionType(u.Right, context, diagnostics);
        
        if (operandType == "unknown")
        {
            return "unknown";
        }

        if (!operandType.EndsWith("*"))
        {
            diagnostics.Add(new(
                context.CompilationUnit.FilePath,
                $"Cannot dereference non-pointer type '{operandType}'.",
                u.Operator.Line,
                u.Operator.Column));

            return "unknown";
        }

        return operandType[..^1];
    }
}