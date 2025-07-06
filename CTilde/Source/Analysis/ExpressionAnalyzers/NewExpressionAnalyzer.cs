using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public class NewExpressionAnalyzer : ExpressionAnalyzerBase
{
    public NewExpressionAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var n = (NewExpressionNode)expr;

        string? typeName = ResolveAndValidateType(n, context, diagnostics);
        
        if (typeName is null)
        {
            return "unknown";
        }

        AnalyzeConstructorArguments(n, context, diagnostics);

        return typeName + "*";
    }

    private string? ResolveAndValidateType(NewExpressionNode n, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        string typeName;

        try
        {
            typeName = _typeResolver.ResolveType(n.Type, context.CurrentFunction.Namespace, context.CompilationUnit);
        }
        catch (InvalidOperationException ex)
        {
            Token token = n.Type.GetFirstToken();
            diagnostics.Add(new(context.CompilationUnit.FilePath, ex.Message, token.Line, token.Column));
            return null;
        }

        if (n.Type is SimpleTypeNode stn && stn.TypeToken.Type == TokenType.Keyword)
        {
            Token token = n.Type.GetFirstToken();

            diagnostics.Add(new(
                context.CompilationUnit.FilePath,
                $"'new' cannot be used with primitive type '{stn.TypeToken.Value}'.",
                token.Line,
                token.Column));

            return null;
        }

        return typeName;
    }

    private void AnalyzeConstructorArguments(NewExpressionNode n, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        foreach (ExpressionNode arg in n.Arguments)
        {
            _semanticAnalyzer.AnalyzeExpressionType(arg, context, diagnostics);
        }
    }
}