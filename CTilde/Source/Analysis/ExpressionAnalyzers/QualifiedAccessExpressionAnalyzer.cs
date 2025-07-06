using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public class QualifiedAccessExpressionAnalyzer : ExpressionAnalyzerBase
{
    public QualifiedAccessExpressionAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var q = (QualifiedAccessExpressionNode)expr;
        string qualifier = TypeResolver.ResolveQualifier(q.Left);
        string memberName = q.Member.Value;

        string? enumAnalysisResult = AnalyzeAsEnumMember(q, qualifier, memberName, context, diagnostics);

        if (enumAnalysisResult is not null)
        {
            return enumAnalysisResult;
        }

        string? functionAnalysisResult = AnalyzeAsFunctionPointer(q, context);

        if (functionAnalysisResult is not null)
        {
            return functionAnalysisResult;
        }

        diagnostics.Add(new(
            context.CompilationUnit.FilePath,
            $"Qualified access '{qualifier}::{memberName}' cannot be evaluated as a value. Only enum members or static function references are supported.",
            q.Member.Line,
            q.Member.Column));

        return "unknown";
    }

    private string? AnalyzeAsEnumMember(QualifiedAccessExpressionNode q, string qualifier, string memberName, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        string? enumTypeFQN = _typeResolver.ResolveEnumTypeName(qualifier, context.CurrentFunction?.Namespace, context.CompilationUnit);
        
        if (enumTypeFQN is null)
        {
            return null;
        }

        if (_functionResolver.GetEnumValue(enumTypeFQN, memberName).HasValue)
        {
            return "int";
        }

        diagnostics.Add(new(
            context.CompilationUnit.FilePath,
            $"Enum '{qualifier}' (resolved to '{enumTypeFQN}') does not contain member '{memberName}'.",
            q.Member.Line,
            q.Member.Column));

        return "unknown";
    }

    private string? AnalyzeAsFunctionPointer(QualifiedAccessExpressionNode q, AnalysisContext context)
    {
        try
        {
            _functionResolver.ResolveFunctionCall(q, context);
            return "void*";
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}