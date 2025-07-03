using System;
using System.Collections.Generic;
using System.Linq;
using CTilde.Diagnostics;

namespace CTilde;

public class SemanticAnalyzer
{
    private readonly TypeRepository _typeRepository;
    private readonly TypeResolver _typeResolver;
    private readonly FunctionResolver _functionResolver;
    private readonly MemoryLayoutManager _memoryLayoutManager;

    public SemanticAnalyzer(TypeRepository typeRepository, TypeResolver typeResolver, FunctionResolver functionResolver, MemoryLayoutManager memoryLayoutManager)
    {
        _typeRepository = typeRepository;
        _typeResolver = typeResolver;
        _functionResolver = functionResolver;
        _memoryLayoutManager = memoryLayoutManager;
    }

    // New overload for use during code generation.
    // No new diagnostics are expected at this stage. If any are found, it indicates
    // a compiler bug, so we throw an exception.
    public string AnalyzeExpressionType(ExpressionNode expr, AnalysisContext context)
    {
        var diagnostics = new List<Diagnostic>();
        var type = AnalyzeExpressionType(expr, context, diagnostics);
        if (diagnostics.Any())
        {
            throw new InvalidOperationException($"Internal Compiler Error: Unexpected semantic error during code generation: {diagnostics.First().Message}");
        }
        return type;
    }

    public string AnalyzeExpressionType(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        try
        {
            return expr switch
            {
                IntegerLiteralNode => "int",
                StringLiteralNode => "char*",
                VariableExpressionNode v => AnalyzeVariableExpression(v, context, diagnostics),
                AssignmentExpressionNode a => AnalyzeAssignmentExpression(a, context, diagnostics),
                MemberAccessExpressionNode ma => AnalyzeMemberAccessExpression(ma, context, diagnostics),
                UnaryExpressionNode u => AnalyzeUnaryExpression(u, context, diagnostics),
                CallExpressionNode c => AnalyzeCallExpression(c, context, diagnostics),
                QualifiedAccessExpressionNode q => AnalyzeQualifiedAccessExpression(q, context, diagnostics),
                NewExpressionNode n => AnalyzeNewExpression(n, context, diagnostics),
                BinaryExpressionNode bin => AnalyzeBinaryExpression(bin, context, diagnostics),
                InitializerListExpressionNode il => AnalyzeInitializerListExpression(il, context, diagnostics),
                _ => throw new NotImplementedException($"AnalyzeExpressionType not implemented for {expr.GetType().Name}")
            };
        }
        catch (InvalidOperationException ex)
        {
            // The logic below still relies on some exceptions for flow control.
            // We convert them to diagnostics here.
            var token = AstHelper.GetFirstToken(expr);
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, ex.Message, token.Line, token.Column));
            return "unknown"; // Return a sentinel type on error
        }
    }

    private string AnalyzeAssignmentExpression(AssignmentExpressionNode a, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var leftType = AnalyzeExpressionType(a.Left, context, diagnostics);
        var rightType = AnalyzeExpressionType(a.Right, context, diagnostics);

        // Allow int to pointer conversion (for malloc etc)
        bool isIntToPointerConversion = leftType.EndsWith("*") && rightType == "int";
        // Allow int literal to char conversion
        bool isIntToCharLiteralConversion = leftType == "char" && rightType == "int" && a.Right is IntegerLiteralNode;

        if (rightType != "unknown" && leftType != "unknown" && leftType != rightType && !isIntToPointerConversion && !isIntToCharLiteralConversion)
        {
            var token = AstHelper.GetFirstToken(a.Right);
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Cannot implicitly convert type '{rightType}' to '{leftType}'.", token.Line, token.Column));
        }

        return leftType; // Type of assignment is type of l-value
    }


    private string AnalyzeInitializerListExpression(InitializerListExpressionNode il, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        // An initializer list cannot be used as a standalone expression.
        // Its validity is checked within AnalyzeDeclarationStatement.
        diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, "Initializer lists can only be used to initialize a variable.", il.OpeningBrace.Line, il.OpeningBrace.Column));
        return "unknown";
    }

    public void AnalyzeDeclarationStatement(DeclarationStatementNode decl, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        string declaredTypeFqn;
        string rawTypeName = TypeRepository.GetTypeNameFromToken(decl.Type, decl.PointerLevel);
        string baseTypeName = rawTypeName.TrimEnd('*');
        string pointerSuffix = new string('*', decl.PointerLevel); // Correctly calculate pointer suffix from level

        if (decl.Type.Type == TokenType.Keyword)
        {
            declaredTypeFqn = rawTypeName; // For primitives like int, char, void
        }
        else
        {
            try
            {
                // For user-defined types (structs), resolve their FQN
                declaredTypeFqn = _typeResolver.ResolveTypeName(baseTypeName, context.CurrentFunction.Namespace, context.CompilationUnit) + pointerSuffix;
            }
            catch (InvalidOperationException ex)
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, ex.Message, decl.Type.Line, decl.Type.Column));
                declaredTypeFqn = "unknown"; // Sentinel type
            }
        }

        if (declaredTypeFqn == "unknown") return; // Stop analysis if type resolution failed

        if (decl.Initializer is InitializerListExpressionNode il)
        {
            var structDef = _typeRepository.FindStruct(declaredTypeFqn);

            if (structDef == null)
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Type '{declaredTypeFqn}' is not a struct and cannot be initialized with an initializer list.", decl.Type.Line, decl.Type.Column));
                return;
            }

            var members = _memoryLayoutManager.GetAllMembers(declaredTypeFqn, context.CompilationUnit);
            if (il.Values.Count > members.Count)
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Too many elements in initializer list for type '{structDef.Name}'.", il.OpeningBrace.Line, il.OpeningBrace.Column));
                // Continue to check existing members for other errors
            }

            for (int i = 0; i < Math.Min(il.Values.Count, members.Count); i++) // Only iterate up to min to avoid IndexOutOfRangeException on too many values
            {
                var member = members[i];
                var valueExpr = il.Values[i];
                var valueType = AnalyzeExpressionType(valueExpr, context, diagnostics);

                // Allow int literal to char conversion
                bool isIntToCharLiteralConversion =
                    valueType == "int" && member.type == "char" && valueExpr is IntegerLiteralNode;

                if (valueType != "unknown" && member.type != valueType && !isIntToCharLiteralConversion)
                {
                    var token = AstHelper.GetFirstToken(valueExpr);
                    diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Cannot initialize member '{member.name}' (type '{member.type}') with a value of type '{valueType}'.", token.Line, token.Column));
                }
            }
        }
        else if (decl.Initializer != null)
        {
            // Analyze other initializers (e.g., assignment)
            var initializerType = AnalyzeExpressionType(decl.Initializer, context, diagnostics);

            // Allow int literal to char conversion
            bool isIntToCharLiteralConversion = declaredTypeFqn == "char" && initializerType == "int" && decl.Initializer is IntegerLiteralNode;

            // Allow int to pointer conversion (for malloc etc)
            bool isIntToPointerConversion = declaredTypeFqn.EndsWith("*") && initializerType == "int";

            if (initializerType != "unknown" && declaredTypeFqn != initializerType && !isIntToCharLiteralConversion && !isIntToPointerConversion)
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Cannot implicitly convert type '{initializerType}' to '{declaredTypeFqn}'.", AstHelper.GetFirstToken(decl.Initializer).Line, AstHelper.GetFirstToken(decl.Initializer).Column));
            }
        }
        else if (decl.ConstructorArguments != null)
        {
            foreach (var arg in decl.ConstructorArguments)
            {
                AnalyzeExpressionType(arg, context, diagnostics);
            }
            // TODO: Add constructor resolution and signature matching check here
        }
    }


    public string AnalyzeFunctionReturnType(FunctionDeclarationNode func, AnalysisContext context)
    {
        var returnTypeNameRaw = TypeRepository.GetTypeNameFromToken(func.ReturnType, func.ReturnPointerLevel);
        string resolvedReturnName;

        if (func.ReturnType.Type == TokenType.Keyword)
        {
            resolvedReturnName = returnTypeNameRaw;
        }
        else
        {
            string baseReturnName = returnTypeNameRaw.TrimEnd('*');
            string pointerSuffix = new string('*', returnTypeNameRaw.Length - baseReturnName.Length);
            resolvedReturnName = _typeResolver.ResolveTypeName(baseReturnName, func.Namespace, context.CompilationUnit) + pointerSuffix;
        }

        return resolvedReturnName;
    }

    private string AnalyzeBinaryExpression(BinaryExpressionNode bin, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var leftTypeFqn = AnalyzeExpressionType(bin.Left, context, diagnostics);
        var rightTypeFqn = AnalyzeExpressionType(bin.Right, context, diagnostics);

        if (leftTypeFqn == "unknown" || rightTypeFqn == "unknown") return "unknown";

        // Handle pointer arithmetic
        if (bin.Operator.Type is TokenType.Plus or TokenType.Minus)
        {
            if (leftTypeFqn.EndsWith("*") && rightTypeFqn == "int")
            {
                return leftTypeFqn; // e.g., char* + int -> char*
            }
            if (leftTypeFqn == "int" && rightTypeFqn.EndsWith("*") && bin.Operator.Type == TokenType.Plus)
            {
                return rightTypeFqn; // e.g., int + char* -> char*
            }
            // Pointer subtraction (ptr - ptr -> int)
            if (leftTypeFqn.EndsWith("*") && rightTypeFqn.EndsWith("*") && bin.Operator.Type == TokenType.Minus)
            {
                // TODO: Check if base types are compatible
                return "int";
            }
        }

        if (_typeRepository.IsStruct(leftTypeFqn))
        {
            try
            {
                var opName = $"operator_{NameMangler.MangleOperator(bin.Operator.Value)}";
                var overload = _functionResolver.ResolveMethod(leftTypeFqn, opName);

                if (overload != null)
                {
                    return AnalyzeFunctionReturnType(overload, context);
                }
            }
            catch (NotImplementedException)
            {
                // This operator is not overloadable.
            }
            // Error handling for missing operator overload
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Operator '{bin.Operator.Value}' is not defined for type '{leftTypeFqn}'.", bin.Operator.Line, bin.Operator.Column));
            return "unknown"; // Sentinel type
        }

        // For other primitive operations (int + int, comparisons, etc.), the result is always int.
        return "int";
    }

    private string AnalyzeNewExpression(NewExpressionNode n, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        // A new expression always returns a pointer to the type.
        string typeName;
        string baseTypeName = n.Type.Value.TrimEnd('*');
        string pointerSuffix = new string('*', n.Type.Value.Length - baseTypeName.Length);

        // 'new' can only be used with identifiers (struct types), not keywords like 'int'
        if (n.Type.Type == TokenType.Keyword)
        {
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"'new' cannot be used with primitive type '{n.Type.Value}'.", n.Type.Line, n.Type.Column));
            return "unknown";
        }
        else
        {
            try
            {
                typeName = _typeResolver.ResolveTypeName(baseTypeName, context.CurrentFunction.Namespace, context.CompilationUnit) + pointerSuffix;
            }
            catch (InvalidOperationException ex)
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, ex.Message, n.Type.Line, n.Type.Column));
                typeName = "unknown";
            }
        }
        return typeName + "*";
    }

    private string AnalyzeVariableExpression(VariableExpressionNode v, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        // 1. Check local variables and parameters in the symbol table.
        if (context.Symbols.TryGetSymbol(v.Identifier.Value, out _, out var type, out _))
        {
            return type;
        }

        // 2. Try resolving as an unqualified enum member (e.g., `KEY_D`).
        var unqualifiedEnumValue = _functionResolver.ResolveUnqualifiedEnumMember(v.Identifier.Value, context.CompilationUnit, context.CurrentFunction.Namespace);
        if (unqualifiedEnumValue.HasValue)
        {
            return "int";
        }

        // 3. If in a method, try resolving as an implicit `this->member`.
        if (context.CurrentFunction.OwnerStructName != null)
        {
            string ownerStructFqn = context.CurrentFunction.Namespace != null
                ? $"{context.CurrentFunction.Namespace}::{context.CurrentFunction.OwnerStructName}"
                : context.CurrentFunction.OwnerStructName;

            // Walk up the inheritance chain to find the member
            string? currentStructFqn = ownerStructFqn;
            MemberVariableNode? member = null;
            StructDefinitionNode? definingStruct = null;

            while (currentStructFqn != null)
            {
                var structDef = _typeRepository.FindStruct(currentStructFqn);
                if (structDef == null) break;

                member = structDef.Members.FirstOrDefault(m => m.Name.Value == v.Identifier.Value);
                if (member != null)
                {
                    definingStruct = structDef;
                    break;
                }

                if (string.IsNullOrEmpty(structDef.BaseStructName)) break;

                var unit = _typeRepository.GetCompilationUnitForStruct(currentStructFqn);
                currentStructFqn = _typeResolver.ResolveTypeName(structDef.BaseStructName, structDef.Namespace, unit);
            }

            if (member != null && definingStruct != null)
            {
                if (member.AccessLevel == AccessSpecifier.Private)
                {
                    if (context.CurrentFunction.OwnerStructName != definingStruct.Name || context.CurrentFunction.Namespace != definingStruct.Namespace)
                    {
                        diagnostics.Add(new Diagnostic(
                            context.CompilationUnit.FilePath,
                            $"Member '{definingStruct.Name}::{member.Name.Value}' is private and cannot be accessed from this context.",
                            v.Identifier.Line,
                            v.Identifier.Column
                        ));
                    }
                }
                var (_, memberTypeResolved) = _memoryLayoutManager.GetMemberInfo(ownerStructFqn, v.Identifier.Value, context.CompilationUnit);
                return memberTypeResolved;
            }
        }

        diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Cannot determine type for undefined variable '{v.Identifier.Value}'.", v.Identifier.Line, v.Identifier.Column));
        return "unknown"; // Sentinel value
    }

    private string AnalyzeMemberAccessExpression(MemberAccessExpressionNode ma, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var leftType = AnalyzeExpressionType(ma.Left, context, diagnostics);
        if (leftType == "unknown") return "unknown";

        string baseStructType = leftType.TrimEnd('*');

        // Walk up the inheritance chain to find the member
        string? currentStructFqn = baseStructType;
        MemberVariableNode? member = null;
        StructDefinitionNode? definingStruct = null;

        while (currentStructFqn != null)
        {
            var structDef = _typeRepository.FindStruct(currentStructFqn);
            if (structDef == null)
            {
                // This case should be handled by the initial struct check
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Type '{baseStructType}' is not a struct and has no members.", AstHelper.GetFirstToken(ma.Left).Line, AstHelper.GetFirstToken(ma.Left).Column));
                return "unknown";
            }

            member = structDef.Members.FirstOrDefault(m => m.Name.Value == ma.Member.Value);
            if (member != null)
            {
                definingStruct = structDef;
                break;
            }

            if (string.IsNullOrEmpty(structDef.BaseStructName)) break;

            var unit = _typeRepository.GetCompilationUnitForStruct(currentStructFqn);
            currentStructFqn = _typeResolver.ResolveTypeName(structDef.BaseStructName, structDef.Namespace, unit);
        }

        if (member == null || definingStruct == null)
        {
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Struct '{baseStructType}' has no member named '{ma.Member.Value}'.", ma.Member.Line, ma.Member.Column));
            return "unknown";
        }

        // ** ENFORCE ACCESS SPECIFIER **
        if (member.AccessLevel == AccessSpecifier.Private)
        {
            // Access is allowed only if the current function is a method of the struct that DEFINED the member.
            if (context.CurrentFunction.OwnerStructName != definingStruct.Name || context.CurrentFunction.Namespace != definingStruct.Namespace)
            {
                diagnostics.Add(new Diagnostic(
                    context.CompilationUnit.FilePath,
                    $"Member '{definingStruct.Name}::{member.Name.Value}' is private and cannot be accessed from this context.",
                    ma.Member.Line,
                    ma.Member.Column
                ));
            }
        }

        var (_, resolvedMemberType) = _memoryLayoutManager.GetMemberInfo(baseStructType, ma.Member.Value, context.CompilationUnit);
        return resolvedMemberType;
    }

    private string AnalyzeUnaryExpression(UnaryExpressionNode u, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        if (u.Operator.Type == TokenType.Ampersand) // Address-of operator
        {
            var operandType = AnalyzeExpressionType(u.Right, context, diagnostics);
            if (operandType == "unknown") return "unknown";
            return operandType + "*";
        }

        if (u.Operator.Type == TokenType.Star) // Dereference operator
        {
            var operandType = AnalyzeExpressionType(u.Right, context, diagnostics);
            if (operandType == "unknown") return "unknown";
            if (!operandType.EndsWith("*"))
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Cannot dereference non-pointer type '{operandType}'.", u.Operator.Line, u.Operator.Column));
                return "unknown";
            }
            return operandType[..^1]; // Remove one level of indirection
        }

        // For other unary operators like negation ('-'), the type does not change.
        return AnalyzeExpressionType(u.Right, context, diagnostics);
    }

    private string AnalyzeCallExpression(CallExpressionNode call, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        FunctionDeclarationNode? func;
        if (call.Callee is MemberAccessExpressionNode callMemberAccess) // Method call: myText.draw()
        {
            var ownerTypeName = AnalyzeExpressionType(callMemberAccess.Left, context, diagnostics).TrimEnd('*');
            if (ownerTypeName == "unknown") return "unknown";

            var ownerStruct = _typeRepository.FindStruct(ownerTypeName);
            if (ownerStruct == null)
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Could not find struct definition for '{ownerTypeName}'.", AstHelper.GetFirstToken(callMemberAccess.Left).Line, AstHelper.GetFirstToken(callMemberAccess.Left).Column));
                return "unknown";
            }

            func = _functionResolver.ResolveMethod(ownerTypeName, callMemberAccess.Member.Value);
            if (func == null)
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Method '{callMemberAccess.Member.Value}' not found on struct '{ownerStruct.Name}' or its base classes.", callMemberAccess.Member.Line, callMemberAccess.Member.Column));
                return "unknown";
            }

            // ** ENFORCE ACCESS SPECIFIER FOR METHODS **
            if (func.AccessLevel == AccessSpecifier.Private)
            {
                var definingStructFqn = func.OwnerStructName != null ? (func.Namespace != null ? $"{func.Namespace}::{func.OwnerStructName}" : func.OwnerStructName) : null;
                var definingStruct = definingStructFqn != null ? _typeRepository.FindStruct(definingStructFqn) : null;

                if (definingStruct == null || context.CurrentFunction.OwnerStructName != definingStruct.Name || context.CurrentFunction.Namespace != definingStruct.Namespace)
                {
                    diagnostics.Add(new Diagnostic(
                        context.CompilationUnit.FilePath,
                        $"Method '{ownerStruct.Name}::{func.Name}' is private and cannot be accessed from this context.",
                        callMemberAccess.Member.Line,
                        callMemberAccess.Member.Column
                    ));
                }
            }
        }
        else // Global or namespaced function call: DrawText(), rl::DrawText()
        {
            try
            {
                func = _functionResolver.ResolveFunctionCall(call.Callee, context.CompilationUnit, context.CurrentFunction);
            }
            catch (InvalidOperationException ex)
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, ex.Message, AstHelper.GetFirstToken(call.Callee).Line, AstHelper.GetFirstToken(call.Callee).Column));
                return "unknown";
            }
        }

        // Analyze arguments for their types
        foreach (var arg in call.Arguments)
        {
            AnalyzeExpressionType(arg, context, diagnostics);
        }

        // TODO: Validate argument count and types against function signature

        return AnalyzeFunctionReturnType(func, context);
    }

    private string AnalyzeQualifiedAccessExpression(QualifiedAccessExpressionNode q, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        string qualifier = TypeResolver.ResolveQualifier(q.Left);
        string memberName = q.Member.Value;

        // 1. Try to resolve as an enum member (e.g., raylib::KeyboardKey::KEY_D)
        string? enumTypeFQN = _typeResolver.ResolveEnumTypeName(qualifier, context.CurrentFunction.Namespace, context.CompilationUnit);
        if (enumTypeFQN != null)
        {
            if (_functionResolver.GetEnumValue(enumTypeFQN, memberName).HasValue)
            {
                return "int"; // Enum members are integers.
            }
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Enum '{qualifier}' (resolved to '{enumTypeFQN}') does not contain member '{memberName}'.", q.Member.Line, q.Member.Column));
            return "unknown";
        }

        // 2. Try to resolve as a qualified function (e.g., rl::InitWindow)
        // If it's not an enum, but a qualified *function* name, its type is effectively a function pointer.
        // We resolve it here, but its "type" for now is just void*.
        try
        {
            _functionResolver.ResolveFunctionCall(q, context.CompilationUnit, context.CurrentFunction);
            return "void*"; // Represents a function pointer type
        }
        catch (InvalidOperationException)
        {
            // Not an enum member, not a function. It's an error.
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Qualified access '{qualifier}::{memberName}' cannot be evaluated as a value directly. Only enum members or function calls are supported.", q.Member.Line, q.Member.Column));
            return "unknown";
        }
    }
}