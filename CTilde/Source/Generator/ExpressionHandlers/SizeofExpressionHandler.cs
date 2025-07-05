using CTilde.Generator;

namespace CTilde.Generator.ExpressionHandlers;

public class SizeofExpressionHandler : ExpressionHandlerBase
{
    public SizeofExpressionHandler(CodeGenerator codeGenerator) : base(codeGenerator) { }

    public override void Generate(ExpressionNode expression, AnalysisContext context)
    {
        var s = (SizeofExpressionNode)expression;

        var typeFqn = TypeResolver.ResolveType(s.Type, context.CurrentFunction.Namespace, context.CompilationUnit);
        var size = MemoryLayoutManager.GetSizeOfType(typeFqn, context.CompilationUnit);
        Builder.AppendInstruction($"mov eax, {size}");
    }
}