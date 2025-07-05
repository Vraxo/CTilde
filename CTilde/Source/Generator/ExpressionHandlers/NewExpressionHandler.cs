using System;
using System.Linq;
using CTilde.Generator;

namespace CTilde.Generator.ExpressionHandlers;

public class NewExpressionHandler : ExpressionHandlerBase
{
    public NewExpressionHandler(CodeGenerator codeGenerator) : base(codeGenerator) { }

    public override void Generate(ExpressionNode expression, AnalysisContext context)
    {
        var n = (NewExpressionNode)expression;
        var typeFqn = TypeResolver.ResolveType(n.Type, context.CurrentFunction.Namespace, context.CompilationUnit);
        var size = MemoryLayoutManager.GetSizeOfType(typeFqn, context.CompilationUnit);

        Builder.AppendInstruction($"push {size}", "Push size for malloc");
        Builder.AppendInstruction("call [malloc]");
        Builder.AppendInstruction("add esp, 4", "Clean up malloc arg");
        Builder.AppendInstruction("mov edi, eax", "Save new'd pointer in edi");

        var argTypes = n.Arguments.Select(arg => SemanticAnalyzer.AnalyzeExpressionType(arg, context)).ToList();
        var ctor = FunctionResolver.FindConstructor(typeFqn, argTypes) ?? throw new InvalidOperationException($"No matching constructor for 'new {typeFqn}'");

        if (VTableManager.HasVTable(typeFqn))
        {
            var vtableLabel = NameMangler.GetVTableLabel(typeFqn);
            Builder.AppendInstruction($"mov dword [edi], {vtableLabel}", "Set vtable pointer on heap object");
        }

        int totalArgSize = 0;
        foreach (var arg in n.Arguments.AsEnumerable().Reverse())
        {
            totalArgSize += Dispatcher.PushArgument(arg, context);
        }

        Builder.AppendInstruction("push edi", "Push 'this' pointer for constructor");
        totalArgSize += 4;

        Builder.AppendInstruction($"call {NameMangler.Mangle(ctor)}");
        Builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up ctor args");

        Builder.AppendInstruction("mov eax, edi", "Return pointer to new object in eax");
    }
}