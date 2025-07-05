using System;
using System.Collections.Generic;
using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public class UnaryExpressionAnalyzer : ExpressionAnalyzerBase
{
    public UnaryExpressionAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var u = (UnaryExpressionNode)expr;

        if (u.Operator.Type == TokenType.Ampersand) // Address-of operator
        {
            var operandType = _semanticAnalyzer.AnalyzeExpressionType(u.Right, context, diagnostics);
            if (operandType == "unknown") return "unknown";
            return operandType + "*";
        }

        if (u.Operator.Type == TokenType.Star) // Dereference operator
        {
            var operandType = _semanticAnalyzer.AnalyzeExpressionType(u.Right, context, diagnostics);
            if (operandType == "unknown") return "unknown";
            if (!operandType.EndsWith("*"))
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Cannot dereference non-pointer type '{operandType}'.", u.Operator.Line, u.Operator.Column));
                return "unknown";
            }
            return operandType[..^1]; // Remove one level of indirection
        }

        // For other unary operators like negation ('-'), the type does not change.
        return _semanticAnalyzer.AnalyzeExpressionType(u.Right, context, diagnostics);
    }
}