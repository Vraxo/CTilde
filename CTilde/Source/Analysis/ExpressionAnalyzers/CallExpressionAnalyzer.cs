using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public class CallExpressionAnalyzer : ExpressionAnalyzerBase
{
    public CallExpressionAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var call = (CallExpressionNode)expr;

        FunctionDeclarationNode? func = ResolveFunction(call, context, diagnostics);
        
        if (func is null)
        {
            return "unknown";
        }

        ValidateAccessSpecifier(func, call, context, diagnostics);
        ValidateArgumentCount(func, call, context, diagnostics);
        AnalyzeArguments(call, context, diagnostics);

        return _semanticAnalyzer.GetFunctionReturnType(func, context);
    }

    private FunctionDeclarationNode? ResolveFunction(CallExpressionNode call, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        try
        {
            return _functionResolver.ResolveFunctionCall(call.Callee, context);
        }
        catch (InvalidOperationException ex)
        {
            diagnostics.Add(new(
                context.CompilationUnit.FilePath,
                ex.Message,
                AstHelper.GetFirstToken(call.Callee).Line,
                AstHelper.GetFirstToken(call.Callee).Column));

            return null;
        }
    }

    private void ValidateAccessSpecifier(FunctionDeclarationNode func, CallExpressionNode call, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        if (func.OwnerStructName is null || func.AccessLevel != AccessSpecifier.Private)
        {
            return;
        }

        string? definingStructFqn = _typeRepository.GetFullyQualifiedOwnerName(func);
        string? ownerFqn = _typeRepository.GetFullyQualifiedOwnerName(context.CurrentFunction);

        if (definingStructFqn == ownerFqn)
        {
            return;
        }

        diagnostics.Add(new(
           context.CompilationUnit.FilePath,
           $"Method '{func.Name}' is private and cannot be accessed from this context.",
           AstHelper.GetFirstToken(call.Callee).Line,
           AstHelper.GetFirstToken(call.Callee).Column
       ));
    }

    private static void ValidateArgumentCount(FunctionDeclarationNode func, CallExpressionNode call, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        int expectedArgs = func.OwnerStructName is not null
            ? func.Parameters.Count - 1
            : func.Parameters.Count;

        if (call.Arguments.Count == expectedArgs)
        {
            return;
        }

        diagnostics.Add(new(
            context.CompilationUnit.FilePath,
            $"Wrong number of arguments for call to '{func.Name}'. Expected {expectedArgs}, but got {call.Arguments.Count}.",
            AstHelper.GetFirstToken(call).Line,
            AstHelper.GetFirstToken(call).Column
        ));
    }

    private void AnalyzeArguments(CallExpressionNode call, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        foreach (ExpressionNode arg in call.Arguments)
        {
            _semanticAnalyzer.AnalyzeExpressionType(arg, context, diagnostics);
        }
    }
}