using CTilde.Generator;

namespace CTilde.Generator.ExpressionHandlers;

public class UnaryExpressionHandler : ExpressionHandlerBase
{
    public UnaryExpressionHandler(CodeGenerator codeGenerator) : base(codeGenerator) { }

    public override void Generate(ExpressionNode expression, AnalysisContext context)
    {
        var u = (UnaryExpressionNode)expression;

        if (u.Operator.Type == TokenType.Ampersand)
        {
            LValueGenerator.GenerateLValueAddress(u.Right, context);
            return;
        }

        Dispatcher.GenerateExpression(u.Right, context);
        switch (u.Operator.Type)
        {
            case TokenType.Minus: Builder.AppendInstruction("neg eax"); break;
            case TokenType.Star:
                var type = SemanticAnalyzer.AnalyzeExpressionType(u, context);
                string instruction = MemoryLayoutManager.GetSizeOfType(type, context.CompilationUnit) == 1 ? "movzx eax, byte [eax]" : "mov eax, [eax]";
                Builder.AppendInstruction(instruction);
                break;
        }
    }
}