using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public abstract class ExpressionAnalyzerBase : IExpressionAnalyzer
{
    // The SemanticAnalyzer instance itself for recursive calls
    protected readonly SemanticAnalyzer _semanticAnalyzer;
    protected readonly TypeRepository _typeRepository;
    protected readonly TypeResolver _typeResolver;
    protected readonly FunctionResolver _functionResolver;
    protected readonly MemoryLayoutManager _memoryLayoutManager;

    protected ExpressionAnalyzerBase(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
    {
        _semanticAnalyzer = semanticAnalyzer;
        _typeRepository = typeRepository;
        _typeResolver = typeResolver;
        _functionResolver = functionResolver;
        _memoryLayoutManager = memoryLayoutManager;
    }

    public abstract string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics);
}