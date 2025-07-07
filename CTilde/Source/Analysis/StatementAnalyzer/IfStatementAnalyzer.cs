using CTilde.Diagnostics;

namespace CTilde.Analysis.StatementAnalyzers;

public class IfStatementAnalyzer : StatementAnalyzerBase
{
    public IfStatementAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override void Analyze(StatementNode stmt, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var ifStmt = (IfStatementNode)stmt;
        _semanticAnalyzer.AnalyzeExpressionType(ifStmt.Condition, context, diagnostics);
    }
}