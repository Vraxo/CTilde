using System;
using System.Linq;
using CTilde.Generator;

namespace CTilde.Generator.ExpressionHandlers;

public class BinaryExpressionHandler : ExpressionHandlerBase
{
    public BinaryExpressionHandler(CodeGenerator codeGenerator) : base(codeGenerator) { }

    public override void Generate(ExpressionNode expression, AnalysisContext context)
    {
        var binExpr = (BinaryExpressionNode)expression;

        var leftTypeFqn = SemanticAnalyzer.AnalyzeExpressionType(binExpr.Left, context);
        var rightTypeFqn = SemanticAnalyzer.AnalyzeExpressionType(binExpr.Right, context);

        if (TypeRepository.IsStruct(leftTypeFqn) && !leftTypeFqn.EndsWith("*"))
        {
            // Handle struct operator overloads
            var opName = $"operator_{NameMangler.MangleOperator(binExpr.Operator.Value)}";
            var overload = FunctionResolver.FindMethod(leftTypeFqn.TrimEnd('*'), opName) ?? throw new InvalidOperationException($"Internal compiler error: overload for '{opName}' not found.");

            var returnType = SemanticAnalyzer.GetFunctionReturnType(overload, context);
            bool returnsStructByValue = TypeRepository.IsStruct(returnType) && !returnType.EndsWith("*");
            int totalArgSize = 0;

            if (returnsStructByValue)
            {
                var size = MemoryLayoutManager.GetSizeOfType(returnType, context.CompilationUnit);
                Builder.AppendInstruction($"sub esp, {size}", "Make space for op+ return value");
                Builder.AppendInstruction("push esp", "Push hidden return value pointer");
                totalArgSize += 4;
            }

            totalArgSize += Dispatcher.PushArgument(binExpr.Right, context);

            LValueGenerator.GenerateLValueAddress(binExpr.Left, context);
            Builder.AppendInstruction("push eax", "Push 'this' pointer");
            totalArgSize += 4;

            Builder.AppendInstruction($"call {NameMangler.Mangle(overload)}");
            Builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up op+ args");

            if (returnsStructByValue)
            {
                Builder.AppendInstruction("lea eax, [esp]", "Get address of hidden return temporary");
            }
            return;
        }

        // Standard evaluation: Right then Left
        Dispatcher.GenerateExpression(binExpr.Right, context);
        Builder.AppendInstruction("push eax");
        Dispatcher.GenerateExpression(binExpr.Left, context);
        Builder.AppendInstruction("pop ecx"); // EAX = Left, ECX = Right

        // Handle pointer arithmetic scaling
        if (binExpr.Operator.Type is TokenType.Plus or TokenType.Minus)
        {
            if (leftTypeFqn.EndsWith("*") && rightTypeFqn == "int")
            {
                var baseType = leftTypeFqn[..^1]; // Remove one level of indirection
                var elementSize = MemoryLayoutManager.GetSizeOfType(baseType, context.CompilationUnit);
                if (elementSize > 1) Builder.AppendInstruction($"imul ecx, {elementSize}");
            }
            else if (leftTypeFqn == "int" && rightTypeFqn.EndsWith("*"))
            {
                var baseType = rightTypeFqn[..^1]; // Remove one level of indirection
                var elementSize = MemoryLayoutManager.GetSizeOfType(baseType, context.CompilationUnit);
                if (elementSize > 1) Builder.AppendInstruction($"imul eax, {elementSize}");
            }
        }

        // Perform Operation
        switch (binExpr.Operator.Type)
        {
            case TokenType.Plus: Builder.AppendInstruction("add eax, ecx"); break;
            case TokenType.Minus: Builder.AppendInstruction("sub eax, ecx"); break;
            case TokenType.Star: Builder.AppendInstruction("imul eax, ecx"); break;
            case TokenType.Slash: Builder.AppendInstruction("cdq"); Builder.AppendInstruction("idiv ecx"); break;
            case TokenType.DoubleEquals: Builder.AppendInstruction("cmp eax, ecx"); Builder.AppendInstruction("sete al"); Builder.AppendInstruction("movzx eax, al"); break;
            case TokenType.NotEquals: Builder.AppendInstruction("cmp eax, ecx"); Builder.AppendInstruction("setne al"); Builder.AppendInstruction("movzx eax, al"); break;
            case TokenType.LessThan: Builder.AppendInstruction("cmp eax, ecx"); Builder.AppendInstruction("setl al"); Builder.AppendInstruction("movzx eax, al"); break;
            case TokenType.GreaterThan: Builder.AppendInstruction("cmp eax, ecx"); Builder.AppendInstruction("setg al"); Builder.AppendInstruction("movzx eax, al"); break;
            default: throw new NotImplementedException($"Op: {binExpr.Operator.Type}");
        }
    }
}