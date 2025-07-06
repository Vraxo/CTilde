using System; // Added for InvalidOperationException
using System.Collections.Generic; // Added for List
using System.Linq;
using CTilde.Diagnostics; // Added for Diagnostic

namespace CTilde.Analysis.ExpressionAnalyzers; // Corrected namespace

public class CallExpressionAnalyzer : ExpressionAnalyzerBase // Corrected class name
{
    public CallExpressionAnalyzer( // Corrected constructor name
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var call = (CallExpressionNode)expr;
        FunctionDeclarationNode? func = null;
        string funcNameForDiags;

        try
        {
            func = _functionResolver.ResolveFunctionCall(call.Callee, context);
            funcNameForDiags = func.Name;
        }
        catch (InvalidOperationException ex)
        {
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, ex.Message, AstHelper.GetFirstToken(call.Callee).Line, AstHelper.GetFirstToken(call.Callee).Column));
            return "unknown";
        }

        // ** ENFORCE ACCESS SPECIFIER FOR METHODS **
        if (func.OwnerStructName != null && func.AccessLevel == AccessSpecifier.Private)
        {
            var definingStructFqn = _typeRepository.GetFullyQualifiedOwnerName(func);
            var ownerFqn = _typeRepository.GetFullyQualifiedOwnerName(context.CurrentFunction);

            if (definingStructFqn != ownerFqn)
            {
                diagnostics.Add(new Diagnostic(
                   context.CompilationUnit.FilePath,
                   $"Method '{func.Name}' is private and cannot be accessed from this context.",
                   AstHelper.GetFirstToken(call.Callee).Line,
                   AstHelper.GetFirstToken(call.Callee).Column
               ));
            }
        }

        // ** VALIDATE ARGUMENT COUNT **
        int expectedArgs = func.OwnerStructName != null
            ? func.Parameters.Count - 1 // Don't count implicit 'this'
            : func.Parameters.Count;

        if (call.Arguments.Count != expectedArgs)
        {
            diagnostics.Add(new Diagnostic(
                context.CompilationUnit.FilePath,
                $"Wrong number of arguments for call to '{func.Name}'. Expected {expectedArgs}, but got {call.Arguments.Count}.",
                AstHelper.GetFirstToken(call).Line,
                AstHelper.GetFirstToken(call).Column
            ));
        }


        // Analyze arguments for their types
        foreach (var arg in call.Arguments)
        {
            _semanticAnalyzer.AnalyzeExpressionType(arg, context, diagnostics);
        }

        // TODO: Validate argument types against function signature

        return _semanticAnalyzer.GetFunctionReturnType(func, context);
    }
}