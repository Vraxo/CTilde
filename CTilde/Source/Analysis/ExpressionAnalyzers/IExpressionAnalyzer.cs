using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public interface IExpressionAnalyzer
{
    string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics);
}