using System;
using CTilde.Generator;

namespace CTilde.Generator.ExpressionHandlers;

public class VariableExpressionHandler : ExpressionHandlerBase
{
    public VariableExpressionHandler(CodeGenerator codeGenerator) : base(codeGenerator) { }

    public override void Generate(ExpressionNode expression, AnalysisContext context)
    {
        var varExpr = (VariableExpressionNode)expression;

        if (context.Symbols.TryGetSymbol(varExpr.Identifier.Value, out int offset, out string type, out _))
        {
            if (TypeRepository.IsStruct(type) && !type.EndsWith("*"))
            {
                // If it's a struct by value, the "value" of the expression is its address.
                LValueGenerator.GenerateLValueAddress(varExpr, context);
            }
            else
            {
                string sign = offset > 0 ? "+" : "";
                string instruction = MemoryLayoutManager.GetSizeOfType(type, context.CompilationUnit) == 1 ? "movzx eax, byte" : "mov eax,";
                Builder.AppendInstruction($"{instruction} [ebp {sign} {offset}]", $"Load value of {varExpr.Identifier.Value}");
            }
            return;
        }

        var enumValue = FunctionResolver.ResolveUnqualifiedEnumMember(varExpr.Identifier.Value, context.CompilationUnit, context.CurrentFunction?.Namespace);
        if (enumValue.HasValue)
        {
            Builder.AppendInstruction($"mov eax, {enumValue.Value}", $"Enum member {varExpr.Identifier.Value}");
            return;
        }

        if (context.CurrentFunction?.OwnerStructName is not null)
        {
            // Re-route implicit `this->member` access to the member access handler.
            var thisExpr = new VariableExpressionNode(new Token(TokenType.Identifier, "this", -1, -1));
            var memberAccessExpr = new MemberAccessExpressionNode(thisExpr, new Token(TokenType.Arrow, "->", -1, -1), varExpr.Identifier) { Parent = varExpr.Parent };
            Dispatcher.GenerateExpression(memberAccessExpr, context);
            return;
        }
        throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'.");
    }
}