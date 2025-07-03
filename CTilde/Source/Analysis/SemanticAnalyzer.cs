using System;
using System.Linq;

namespace CTilde;

public class SemanticAnalyzer
{
    private readonly TypeManager _typeManager;

    public SemanticAnalyzer(TypeManager typeManager)
    {
        _typeManager = typeManager;
    }

    public string AnalyzeExpressionType(ExpressionNode expr, SymbolTable symbols, CompilationUnitNode context, FunctionDeclarationNode currentFunction)
    {
        switch (expr)
        {
            case IntegerLiteralNode: return "int";
            case StringLiteralNode: return "char*";
            case VariableExpressionNode v:
                if (symbols.TryGetSymbol(v.Identifier.Value, out _, out _, out _)) return symbols.GetSymbolType(v.Identifier.Value);

                // Try resolving as an unqualified enum member
                var unqualifiedEnumValue = _typeManager.ResolveUnqualifiedEnumMember(v.Identifier.Value, context, currentFunction.Namespace);
                if (unqualifiedEnumValue.HasValue) return "int";

                if (currentFunction.OwnerStructName != null)
                {
                    try
                    {
                        (StructDefinitionNode Def, string FullName) = _typeManager.GetStructTypeFromUnqualifiedName(currentFunction.OwnerStructName, currentFunction.Namespace);
                        (int _, string memberTypeResolved) = _typeManager.GetMemberInfo(FullName, v.Identifier.Value, context);
                        return memberTypeResolved;
                    }
                    catch (InvalidOperationException) { /* Fall through */ }
                }
                throw new InvalidOperationException($"Cannot determine type for undefined variable '{v.Identifier.Value}'.");

            case AssignmentExpressionNode a: return AnalyzeExpressionType(a.Left, symbols, context, currentFunction);

            case MemberAccessExpressionNode ma:
                var leftType = AnalyzeExpressionType(ma.Left, symbols, context, currentFunction);
                string baseStructType = leftType.TrimEnd('*');
                var (_, resolvedMemberType) = _typeManager.GetMemberInfo(baseStructType, ma.Member.Value, context);
                return resolvedMemberType;

            case UnaryExpressionNode u when u.Operator.Type == TokenType.Ampersand:
                return AnalyzeExpressionType(u.Right, symbols, context, currentFunction) + "*";

            case UnaryExpressionNode u when u.Operator.Type == TokenType.Star:
                var operandType = AnalyzeExpressionType(u.Right, symbols, context, currentFunction);
                if (!operandType.EndsWith("*")) throw new InvalidOperationException($"Cannot dereference non-pointer type '{operandType}'.");
                return operandType.Substring(0, operandType.Length - 1);

            case CallExpressionNode call:
                FunctionDeclarationNode func;
                if (call.Callee is MemberAccessExpressionNode callMemberAccess)
                {
                    var ownerTypeName = AnalyzeExpressionType(callMemberAccess.Left, symbols, context, currentFunction).TrimEnd('*');
                    var (ownerStruct, _) = _typeManager.GetStructTypeFromFullName(ownerTypeName);
                    func = _typeManager.ResolveMethod(ownerStruct, callMemberAccess.Member.Value);
                }
                else
                {
                    func = _typeManager.ResolveFunctionCall(call.Callee, context, currentFunction);
                }

                string returnTypeNameRaw = _typeManager.GetTypeName(func.ReturnType, func.ReturnPointerLevel);
                string baseReturnName = returnTypeNameRaw.TrimEnd('*');
                string returnPointerSuffix = new('*', returnTypeNameRaw.Length - baseReturnName.Length);

                if (func.ReturnType.Type != TokenType.Keyword && !baseReturnName.Equals("void", StringComparison.OrdinalIgnoreCase))
                {
                    return _typeManager.ResolveTypeName(baseReturnName, func.Namespace, context) + returnPointerSuffix;
                }
                return returnTypeNameRaw;

            case QualifiedAccessExpressionNode q:
                // Check if the qualifier refers to an enum type name.
                string? enumTypeFQN = _typeManager.ResolveEnumTypeName(q.Namespace.Value, currentFunction.Namespace, context);
                if (enumTypeFQN != null)
                {
                    if (_typeManager.GetEnumValue(enumTypeFQN, q.Member.Value).HasValue)
                    {
                        return "int"; // Enum members are integers
                    }
                    // If it resolved to an enum type, but the member isn't found, still an enum context problem
                    throw new InvalidOperationException($"Enum '{q.Namespace.Value}' does not contain member '{q.Member.Value}'.");
                }
                // If not an enum type, it must be a function call target
                // For a function target, AnalyzeExpressionType isn't directly applicable, as it returns the *function itself*, not its return type.
                // This QualifiedAccessExpressionNode might be the Callee of a CallExpressionNode.
                // In that context, CallExpressionNode handles the function resolution and return type.
                // If it's a standalone QualifiedAccessExpressionNode, it's likely an error unless it's an enum.
                throw new InvalidOperationException($"Qualified access expression '{q.Namespace.Value}::{q.Member.Value}' cannot be evaluated as a value directly. Expected an enum member or part of a function call.");

            case BinaryExpressionNode: return "int";

            default: throw new NotImplementedException($"AnalyzeExpressionType not implemented for {expr.GetType().Name}");
        }
    }
}