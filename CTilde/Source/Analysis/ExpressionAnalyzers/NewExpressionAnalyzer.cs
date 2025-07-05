using System;
using System.Collections.Generic;
using System.Linq;
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
        string typeName;
        try
        {
            typeName = _typeResolver.ResolveType(n.Type, context.CurrentFunction.Namespace, context.CompilationUnit);
        }
        catch (InvalidOperationException ex)
        {
            var token = n.Type.GetFirstToken();
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, ex.Message, token.Line, token.Column));
            typeName = "unknown";
        }

        // 'new' can only be used with struct types, not primitives like 'int'
        if (n.Type is SimpleTypeNode stn && stn.TypeToken.Type == TokenType.Keyword)
        {
            var token = n.Type.GetFirstToken();
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"'new' cannot be used with primitive type '{stn.TypeToken.Value}'.", token.Line, token.Column));
            return "unknown";
        }

        // Analyze constructor arguments to mark variables as used and check for other errors.
        foreach (var arg in n.Arguments)
        {
            _semanticAnalyzer.AnalyzeExpressionType(arg, context, diagnostics);
        }
        // TODO: Add constructor resolution and signature matching check here

        return typeName + "*";
    }
}