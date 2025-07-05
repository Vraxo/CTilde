using CTilde.Generator;

namespace CTilde.Generator.ExpressionHandlers;

public class AssignmentExpressionHandler : ExpressionHandlerBase
{
    public AssignmentExpressionHandler(CodeGenerator codeGenerator) : base(codeGenerator) { }

    public override void Generate(ExpressionNode expression, AnalysisContext context)
    {
        var assign = (AssignmentExpressionNode)expression;

        string lValueType = SemanticAnalyzer.AnalyzeExpressionType(assign.Left, context);
        bool isStructAssign = TypeRepository.IsStruct(lValueType) && !lValueType.EndsWith("*");

        if (isStructAssign)
        {
            Dispatcher.GenerateExpression(assign.Right, context);
            Builder.AppendInstruction("push eax", "Push source address");
            LValueGenerator.GenerateLValueAddress(assign.Left, context);
            Builder.AppendInstruction("mov edi, eax", "Pop destination into EDI");
            Builder.AppendInstruction("pop esi", "Pop source into ESI");
            int size = MemoryLayoutManager.GetSizeOfType(lValueType, context.CompilationUnit);
            Builder.AppendInstruction($"push {size}");
            Builder.AppendInstruction("push esi");
            Builder.AppendInstruction("push edi");
            Builder.AppendInstruction("call [memcpy]");
            Builder.AppendInstruction("add esp, 12");
        }
        else
        {
            Dispatcher.GenerateExpression(assign.Right, context);
            Builder.AppendInstruction("push eax", "Push value");
            LValueGenerator.GenerateLValueAddress(assign.Left, context);
            Builder.AppendInstruction("pop ecx", "Pop value into ECX");
            string instruction = MemoryLayoutManager.GetSizeOfType(lValueType, context.CompilationUnit) == 1 ? "mov [eax], cl" : "mov [eax], ecx";
            Builder.AppendInstruction(instruction, "Assign value");
        }
        // The result of an assignment expression is the assigned value, which is still in ECX.
        // Or if it was a struct, the address is in EAX. Here we handle the non-struct case.
        if (!isStructAssign) Builder.AppendInstruction("mov eax, ecx");
    }
}