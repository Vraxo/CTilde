using CTilde.Diagnostics;

namespace CTilde.Analysis.StatementAnalyzers;

public class WhileStatementAnalyzer : StatementAnalyzerBase
{
    public WhileStatementAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override void Analyze(StatementNode stmt, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var whileStmt = (WhileStatementNode)stmt;
        _semanticAnalyzer.AnalyzeExpressionType(whileStmt.Condition, context, diagnostics);
    }
}