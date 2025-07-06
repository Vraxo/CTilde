using System;
using System.Collections.Generic;
using System.Linq;
using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public class BinaryExpressionAnalyzer : ExpressionAnalyzerBase
{
    public BinaryExpressionAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var bin = (BinaryExpressionNode)expr;

        var leftTypeFqn = _semanticAnalyzer.AnalyzeExpressionType(bin.Left, context, diagnostics);
        var rightTypeFqn = _semanticAnalyzer.AnalyzeExpressionType(bin.Right, context, diagnostics);

        if (leftTypeFqn == "unknown" || rightTypeFqn == "unknown") return "unknown";

        // Handle pointer arithmetic
        if (bin.Operator.Type is TokenType.Plus or TokenType.Minus)
        {
            if (leftTypeFqn.EndsWith("*") && rightTypeFqn == "int")
            {
                return leftTypeFqn; // e.g., char* + int -> char*
            }
            if (leftTypeFqn == "int" && rightTypeFqn.EndsWith("*") && bin.Operator.Type == TokenType.Plus)
            {
                return rightTypeFqn; // e.g., int + char* -> char*
            }
            // Pointer subtraction (ptr - ptr -> int)
            if (leftTypeFqn.EndsWith("*") && rightTypeFqn.EndsWith("*") && bin.Operator.Type == TokenType.Minus)
            {
                // TODO: Check if base types are compatible
                return "int";
            }
        }

        // Handle pointer comparisons
        if (bin.Operator.Type is TokenType.DoubleEquals or TokenType.NotEquals or TokenType.LessThan or TokenType.GreaterThan)
        {
            bool leftIsPtr = leftTypeFqn.EndsWith("*");
            bool rightIsPtr = rightTypeFqn.EndsWith("*");
            bool leftIsInt = leftTypeFqn == "int";
            bool rightIsInt = rightTypeFqn == "int";

            // Allow ptr <=> ptr and ptr <=> int
            if ((leftIsPtr && rightIsPtr) || (leftIsPtr && rightIsInt) || (leftIsInt && rightIsPtr))
            {
                return "int"; // Result of any comparison is an int.
            }
        }

        if (_typeRepository.IsStruct(leftTypeFqn))
        {
            try
            {
                var opName = $"operator_{NameMangler.MangleOperator(bin.Operator.Value)}";
                var overload = _functionResolver.ResolveMethod(leftTypeFqn, opName);

                if (overload is not null)
                {
                    return _semanticAnalyzer.GetFunctionReturnType(overload, context);
                }
            }
            catch (NotImplementedException)
            {
                // This operator is not overloadable.
            }
            // Error handling for missing operator overload
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Operator '{bin.Operator.Value}' is not defined for type '{leftTypeFqn}'.", bin.Operator.Line, bin.Operator.Column));
            return "unknown"; // Sentinel type
        }

        // For other primitive operations (int + int, comparisons, etc.), the result is always int.
        return "int";
    }
}