using CTilde.Diagnostics;

namespace CTilde.Analysis.StatementAnalyzers;

public class ExpressionStatementAnalyzer : StatementAnalyzerBase
{
    public ExpressionStatementAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override void Analyze(StatementNode stmt, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var exprStmt = (ExpressionStatementNode)stmt;
        _semanticAnalyzer.AnalyzeExpressionType(exprStmt.Expression, context, diagnostics);
    }
}