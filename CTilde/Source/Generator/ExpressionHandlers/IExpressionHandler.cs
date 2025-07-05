namespace CTilde.Generator.ExpressionHandlers;

public interface IExpressionHandler
{
    void Generate(ExpressionNode expression, AnalysisContext context);
}