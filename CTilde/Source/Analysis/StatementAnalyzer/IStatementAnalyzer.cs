using CTilde.Diagnostics;

namespace CTilde.Analysis.StatementAnalyzers;

public interface IStatementAnalyzer
{
    void Analyze(StatementNode stmt, AnalysisContext context, List<Diagnostic> diagnostics);
}