using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public class InitializerListExpressionAnalyzer : ExpressionAnalyzerBase
{
    public InitializerListExpressionAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var il = (InitializerListExpressionNode)expr;

        diagnostics.Add(new(
            context.CompilationUnit.FilePath,
            "Initializer lists can only be used to initialize a variable.",
            il.OpeningBrace.Line,
            il.OpeningBrace.Column));

        return "unknown";
    }
}