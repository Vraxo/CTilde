using System.Collections.Generic;
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
        // This method is only called if an initializer list is used as a standalone expression,
        // which is illegal. AnalyzeDeclarationStatement handles the valid case directly.
        diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, "Initializer lists can only be used to initialize a variable.", il.OpeningBrace.Line, il.OpeningBrace.Column));
        return "unknown";
    }
}