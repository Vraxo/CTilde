using CTilde.Generator;

namespace CTilde.Generator.ExpressionHandlers;

public class IntegerLiteralHandler : ExpressionHandlerBase
{
    public IntegerLiteralHandler(CodeGenerator codeGenerator) : base(codeGenerator) { }

    public override void Generate(ExpressionNode expression, AnalysisContext context)
    {
        var literal = (IntegerLiteralNode)expression;
        Builder.AppendInstruction($"mov eax, {literal.Value}");
    }
}