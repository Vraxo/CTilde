using CTilde.Generator;

namespace CTilde.Generator.ExpressionHandlers;

public class StringLiteralHandler : ExpressionHandlerBase
{
    public StringLiteralHandler(CodeGenerator codeGenerator) : base(codeGenerator) { }

    public override void Generate(ExpressionNode expression, AnalysisContext context)
    {
        var str = (StringLiteralNode)expression;
        Builder.AppendInstruction($"mov eax, {str.Label}");
    }
}