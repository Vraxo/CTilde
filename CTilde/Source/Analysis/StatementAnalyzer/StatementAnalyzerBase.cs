using CTilde.Diagnostics;

namespace CTilde.Analysis.StatementAnalyzers;

public abstract class StatementAnalyzerBase : IStatementAnalyzer
{
    protected readonly SemanticAnalyzer _semanticAnalyzer;
    protected readonly TypeRepository _typeRepository;
    protected readonly TypeResolver _typeResolver;
    protected readonly FunctionResolver _functionResolver;
    protected readonly MemoryLayoutManager _memoryLayoutManager;

    protected StatementAnalyzerBase(
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

    public abstract void Analyze(StatementNode stmt, AnalysisContext context, List<Diagnostic> diagnostics);
}