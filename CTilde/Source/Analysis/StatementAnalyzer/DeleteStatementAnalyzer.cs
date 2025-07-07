using CTilde.Diagnostics;

namespace CTilde.Analysis.StatementAnalyzers;

public class DeleteStatementAnalyzer : StatementAnalyzerBase
{
    public DeleteStatementAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override void Analyze(StatementNode stmt, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var deleteStmt = (DeleteStatementNode)stmt;
        string exprType = _semanticAnalyzer.AnalyzeExpressionType(deleteStmt.Expression, context, diagnostics);

        if (exprType == "unknown" || exprType.EndsWith('*'))
        {
            return;
        }

        Token token = AstHelper.GetFirstToken(deleteStmt.Expression);

        diagnostics.Add(new(
            context.CompilationUnit.FilePath,
            $"'delete' operator can only be applied to pointers, not type '{exprType}'.",
            token.Line,
            token.Column));
    }
}