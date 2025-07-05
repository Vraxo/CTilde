using CTilde.Generator;

namespace CTilde.Generator.ExpressionHandlers;

public class MemberAccessExpressionHandler : ExpressionHandlerBase
{
    public MemberAccessExpressionHandler(CodeGenerator codeGenerator) : base(codeGenerator) { }

    public override void Generate(ExpressionNode expression, AnalysisContext context)
    {
        var m = (MemberAccessExpressionNode)expression;

        LValueGenerator.GenerateLValueAddress(m, context);
        var memberType = SemanticAnalyzer.AnalyzeExpressionType(m, context);
        if (TypeRepository.IsStruct(memberType) && !memberType.EndsWith("*"))
        {
            // If the member is a struct by value, the "value" of the expression is its address.
            // The caller (e.g. assignment or function call) will handle it from there.
            return;
        }

        // For primitives or pointers, dereference the address to get the value.
        string instruction = MemoryLayoutManager.GetSizeOfType(memberType, context.CompilationUnit) == 1 ? "movzx eax, byte [eax]" : "mov eax, [eax]";
        Builder.AppendInstruction(instruction);
    }
}