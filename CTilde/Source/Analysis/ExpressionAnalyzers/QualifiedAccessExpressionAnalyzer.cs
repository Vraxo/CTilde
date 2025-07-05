using CTilde.Generator;

namespace CTilde.Generator.ExpressionHandlers;

public class QualifiedAccessExpressionHandler : ExpressionHandlerBase
{
    public QualifiedAccessExpressionHandler(CodeGenerator codeGenerator) : base(codeGenerator) { }

    public override void Generate(ExpressionNode expression, AnalysisContext context)
    {
        var qNode = (QualifiedAccessExpressionNode)expression;

        string potentialEnumTypeName = TypeResolver.ResolveQualifier(qNode.Left);
        string memberName = qNode.Member.Value;
        string? enumTypeFQN = TypeResolver.ResolveEnumTypeName(potentialEnumTypeName, context.CurrentFunction?.Namespace, context.CompilationUnit);
        if (enumTypeFQN != null)
        {
            var enumValue = FunctionResolver.GetEnumValue(enumTypeFQN, memberName);
            if (enumValue.HasValue)
            {
                Builder.AppendInstruction($"mov eax, {enumValue.Value}", $"Enum member {potentialEnumTypeName}::{memberName}");
                return;
            }
        }

        var func = FunctionResolver.ResolveFunctionCall(qNode, context);
        string calleeTarget = func.Body == null ? $"[{func.Name}]" : NameMangler.Mangle(func);
        if (func.Body == null) ExternalFunctions.Add(func.Name);
        Builder.AppendInstruction($"mov eax, {calleeTarget}");
    }
}