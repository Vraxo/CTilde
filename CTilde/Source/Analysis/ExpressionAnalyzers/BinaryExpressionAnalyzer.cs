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

        string leftTypeFqn = _semanticAnalyzer.AnalyzeExpressionType(bin.Left, context, diagnostics);
        string rightTypeFqn = _semanticAnalyzer.AnalyzeExpressionType(bin.Right, context, diagnostics);

        if (leftTypeFqn == "unknown" || rightTypeFqn == "unknown")
        {
            return "unknown";
        }

        string? pointerOperationResult = AnalyzePointerOperation(bin.Operator.Type, leftTypeFqn, rightTypeFqn);
        if (pointerOperationResult is not null)
        {
            return pointerOperationResult;
        }

        if (_typeRepository.IsStruct(leftTypeFqn))
        {
            return AnalyzeStructOperatorOverloading(bin, leftTypeFqn, context, diagnostics);
        }

        return "int";
    }

    private static string? AnalyzePointerOperation(TokenType opType, string leftTypeFqn, string rightTypeFqn)
    {
        bool leftIsPtr = leftTypeFqn.EndsWith("*");
        bool rightIsPtr = rightTypeFqn.EndsWith("*");
        bool leftIsInt = leftTypeFqn == "int";
        bool rightIsInt = rightTypeFqn == "int";

        if (opType is TokenType.Plus or TokenType.Minus)
        {
            if (leftIsPtr && rightIsInt)
            {
                return leftTypeFqn;
            }

            if (leftIsInt && rightIsPtr && opType == TokenType.Plus)
            {
                return rightTypeFqn;
            }

            if (leftIsPtr && rightIsPtr && opType == TokenType.Minus)
            {
                return "int";
            }
        }

        if (opType is TokenType.DoubleEquals or TokenType.NotEquals or TokenType.LessThan or TokenType.GreaterThan)
        {
            if (leftIsPtr && rightIsPtr || leftIsPtr && rightIsInt || leftIsInt && rightIsPtr)
            {
                return "int";
            }
        }

        return null;
    }

    private string AnalyzeStructOperatorOverloading(BinaryExpressionNode bin, string typeFqn, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        try
        {
            string opName = $"operator_{NameMangler.MangleOperator(bin.Operator.Value)}";
            FunctionDeclarationNode? overload = _functionResolver.ResolveMethod(typeFqn, opName);

            if (overload is not null)
            {
                return _semanticAnalyzer.GetFunctionReturnType(overload, context);
            }
        }
        catch (NotImplementedException)
        {
        }

        diagnostics.Add(new(context.CompilationUnit.FilePath, $"Operator '{bin.Operator.Value}' is not defined for type '{typeFqn}'.", bin.Operator.Line, bin.Operator.Column));
        return "unknown";
    }
}