using System;
using System.Linq;

namespace CTilde;

public class SemanticAnalyzer
{
    private readonly TypeManager _typeManager;
    private readonly MemoryLayoutManager _memoryLayoutManager;

    public SemanticAnalyzer(TypeManager typeManager, MemoryLayoutManager memoryLayoutManager)
    {
        _typeManager = typeManager;
        _memoryLayoutManager = memoryLayoutManager;
    }

    public string AnalyzeExpressionType(ExpressionNode expr, AnalysisContext context)
    {
        return expr switch
        {
            IntegerLiteralNode => "int",
            StringLiteralNode => "char*",
            VariableExpressionNode v => AnalyzeVariableExpression(v, context),
            AssignmentExpressionNode a => AnalyzeExpressionType(a.Left, context), // Type of assignment is type of l-value
            MemberAccessExpressionNode ma => AnalyzeMemberAccessExpression(ma, context),
            UnaryExpressionNode u => AnalyzeUnaryExpression(u, context),
            CallExpressionNode c => AnalyzeCallExpression(c, context),
            QualifiedAccessExpressionNode q => AnalyzeQualifiedAccessExpression(q, context),
            NewExpressionNode n => AnalyzeNewExpression(n, context),
            BinaryExpressionNode bin => AnalyzeBinaryExpression(bin, context),
            _ => throw new NotImplementedException($"AnalyzeExpressionType not implemented for {expr.GetType().Name}")
        };
    }

    public string AnalyzeFunctionReturnType(FunctionDeclarationNode func, AnalysisContext context)
    {
        var returnTypeNameRaw = TypeManager.GetTypeName(func.ReturnType, func.ReturnPointerLevel);
        string resolvedReturnName;

        if (func.ReturnType.Type != TokenType.Keyword && !returnTypeNameRaw.StartsWith("void"))
        {
            string baseReturnName = returnTypeNameRaw.TrimEnd('*');
            string pointerSuffix = new string('*', returnTypeNameRaw.Length - baseReturnName.Length);
            resolvedReturnName = _typeManager.ResolveTypeName(baseReturnName, func.Namespace, context.CompilationUnit) + pointerSuffix;
        }
        else
        {
            resolvedReturnName = returnTypeNameRaw;
        }

        if (_typeManager.IsStruct(resolvedReturnName) && !resolvedReturnName.EndsWith("*"))
        {
            // A function returning a struct by value actually returns a pointer
            // to a caller-provided location. The type of the expression is a pointer.
            return resolvedReturnName + "*";
        }

        return resolvedReturnName;
    }

    private string AnalyzeBinaryExpression(BinaryExpressionNode bin, AnalysisContext context)
    {
        var leftTypeFqn = AnalyzeExpressionType(bin.Left, context);

        if (_typeManager.IsStruct(leftTypeFqn))
        {
            try
            {
                var opName = $"operator_{NameMangler.MangleOperator(bin.Operator.Value)}";
                var overload = _typeManager.FindMethod(leftTypeFqn, opName);

                if (overload != null)
                {
                    return AnalyzeFunctionReturnType(overload, context);
                }
            }
            catch (NotImplementedException)
            {
                // This operator is not overloadable.
            }
            throw new InvalidOperationException($"Operator '{bin.Operator.Value}' is not defined for type '{leftTypeFqn}'.");
        }

        // For primitive types like int, the result is always int.
        return "int";
    }

    private string AnalyzeNewExpression(NewExpressionNode n, AnalysisContext context)
    {
        // A new expression always returns a pointer to the type.
        var typeName = _typeManager.ResolveTypeName(n.Type.Value, context.CurrentFunction.Namespace, context.CompilationUnit);
        return typeName + "*";
    }

    private string AnalyzeVariableExpression(VariableExpressionNode v, AnalysisContext context)
    {
        // 1. Check local variables and parameters in the symbol table.
        if (context.Symbols.TryGetSymbol(v.Identifier.Value, out _, out var type, out _))
        {
            return type;
        }

        // 2. Try resolving as an unqualified enum member (e.g., `KEY_D`).
        var unqualifiedEnumValue = _typeManager.ResolveUnqualifiedEnumMember(v.Identifier.Value, context.CompilationUnit, context.CurrentFunction.Namespace);
        if (unqualifiedEnumValue.HasValue)
        {
            return "int";
        }

        // 3. If in a method, try resolving as an implicit `this->member`.
        if (context.CurrentFunction.OwnerStructName != null)
        {
            try
            {
                string ownerStructFqn = context.CurrentFunction.Namespace != null
                    ? $"{context.CurrentFunction.Namespace}::{context.CurrentFunction.OwnerStructName}"
                    : context.CurrentFunction.OwnerStructName;

                (_, string memberTypeResolved) = _memoryLayoutManager.GetMemberInfo(ownerStructFqn, v.Identifier.Value, context.CompilationUnit);
                return memberTypeResolved;
            }
            catch (InvalidOperationException) { /* Fall through to the final error */ }
        }

        throw new InvalidOperationException($"Cannot determine type for undefined variable '{v.Identifier.Value}'.");
    }

    private string AnalyzeMemberAccessExpression(MemberAccessExpressionNode ma, AnalysisContext context)
    {
        var leftType = AnalyzeExpressionType(ma.Left, context);
        string baseStructType = leftType.TrimEnd('*');
        var (_, resolvedMemberType) = _memoryLayoutManager.GetMemberInfo(baseStructType, ma.Member.Value, context.CompilationUnit);
        return resolvedMemberType;
    }

    private string AnalyzeUnaryExpression(UnaryExpressionNode u, AnalysisContext context)
    {
        if (u.Operator.Type == TokenType.Ampersand) // Address-of operator
        {
            var operandType = AnalyzeExpressionType(u.Right, context);
            if (operandType.EndsWith("*"))
            {
                // Taking address of a pointer (e.g. `string**`)
                return operandType + "*";
            }
            // Taking address of a value type (e.g. `string s; &s;` -> `string*`)
            return _typeManager.IsStruct(operandType) ? operandType + "*" : operandType;
        }

        if (u.Operator.Type == TokenType.Star) // Dereference operator
        {
            var operandType = AnalyzeExpressionType(u.Right, context);
            if (!operandType.EndsWith("*"))
            {
                throw new InvalidOperationException($"Cannot dereference non-pointer type '{operandType}'.");
            }
            return operandType[..^1]; // Remove one level of indirection
        }

        // For other unary operators like negation ('-'), the type does not change.
        return AnalyzeExpressionType(u.Right, context);
    }

    private string AnalyzeCallExpression(CallExpressionNode call, AnalysisContext context)
    {
        FunctionDeclarationNode func;
        if (call.Callee is MemberAccessExpressionNode callMemberAccess) // Method call: myText.draw()
        {
            var ownerTypeName = AnalyzeExpressionType(callMemberAccess.Left, context).TrimEnd('*');
            var (ownerStruct, _) = _typeManager.GetStructTypeFromFullName(ownerTypeName);
            func = _typeManager.ResolveMethod(ownerStruct, callMemberAccess.Member.Value);
        }
        else // Global or namespaced function call: DrawText(), rl::DrawText()
        {
            func = _typeManager.ResolveFunctionCall(call.Callee, context.CompilationUnit, context.CurrentFunction);
        }

        return AnalyzeFunctionReturnType(func, context);
    }

    private string ResolveQualifier(ExpressionNode expr)
    {
        return expr switch
        {
            VariableExpressionNode v => v.Identifier.Value,
            QualifiedAccessExpressionNode q => $"{ResolveQualifier(q.Left)}::{q.Member.Value}",
            _ => throw new InvalidOperationException($"Cannot resolve qualifier from expression of type {expr.GetType().Name}")
        };
    }

    private string AnalyzeQualifiedAccessExpression(QualifiedAccessExpressionNode q, AnalysisContext context)
    {
        // This expression is a value, so it must be an enum member.
        // `q.Left` resolves to the enum type (e.g., `raylib::KeyboardKey`)
        // `q.Member` resolves to the enum member (e.g., `KEY_D`)

        string potentialEnumTypeName = ResolveQualifier(q.Left);
        string memberName = q.Member.Value;

        string? enumTypeFQN = _typeManager.ResolveEnumTypeName(potentialEnumTypeName, context.CurrentFunction.Namespace, context.CompilationUnit);
        if (enumTypeFQN != null)
        {
            if (_typeManager.GetEnumValue(enumTypeFQN, memberName).HasValue)
            {
                return "int"; // Enum members are integers.
            }
            throw new InvalidOperationException($"Enum '{potentialEnumTypeName}' (resolved to '{enumTypeFQN}') does not contain member '{memberName}'.");
        }

        // If not an enum, it's a qualified name like `rl::DrawText`. This is not a value on its own.
        // It's only valid as the `Callee` of a `CallExpression`, which is handled by `AnalyzeCallExpression`.
        throw new InvalidOperationException($"Qualified access '{potentialEnumTypeName}::{memberName}' cannot be evaluated as a value directly. Only enum members or function calls are supported.");
    }
}