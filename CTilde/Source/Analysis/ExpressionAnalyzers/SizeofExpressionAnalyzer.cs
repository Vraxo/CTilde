using System.Collections.Generic;
using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public class SizeofExpressionAnalyzer : ExpressionAnalyzerBase
{
    public SizeofExpressionAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        return "int"; // sizeof always returns an int
    }
}