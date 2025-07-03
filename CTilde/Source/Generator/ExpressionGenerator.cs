using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class ExpressionGenerator
{
    private readonly CodeGenerator _context;
    private AssemblyBuilder Builder => _context.Builder;
    private TypeManager TypeManager => _context.TypeManager;
    private SemanticAnalyzer SemanticAnalyzer => _context.SemanticAnalyzer;
    private ProgramNode Program => _context.Program;
    private SymbolTable CurrentSymbols => _context.CurrentSymbols;
    private CompilationUnitNode CurrentCompilationUnit => _context.CurrentCompilationUnit;
    private FunctionDeclarationNode CurrentFunction => _context.CurrentFunction;
    private HashSet<string> ExternalFunctions => _context.ExternalFunctions;

    public ExpressionGenerator(CodeGenerator context)
    {
        _context = context;
    }

    private AnalysisContext CreateAnalysisContext()
    {
        return new AnalysisContext(CurrentSymbols, CurrentCompilationUnit, CurrentFunction);
    }

    public void GenerateLValueAddress(ExpressionNode expression)
    {
        switch (expression)
        {
            case VariableExpressionNode varExpr:
                {
                    if (CurrentSymbols.TryGetSymbol(varExpr.Identifier.Value, out var offset, out _, out _))
                    {
                        string sign = offset > 0 ? "+" : ""; // Offset can be positive (param) or negative (local)
                        Builder.AppendInstruction($"lea eax, [ebp {sign} {offset}]", $"Get address of var/param {varExpr.Identifier.Value}");
                    }
                    else if (CurrentFunction.OwnerStructName != null)
                    {
                        try
                        {
                            var type = TypeManager.GetStructTypeFromUnqualifiedName(CurrentFunction.OwnerStructName, CurrentFunction.Namespace);
                            var (memberOffset, _) = TypeManager.GetMemberInfo(type.FullName, varExpr.Identifier.Value, CurrentCompilationUnit);
                            CurrentSymbols.TryGetSymbol("this", out var thisOffset, out _, out _);
                            Builder.AppendInstruction($"mov eax, [ebp + {thisOffset}]", "Get `this` pointer value");
                            if (memberOffset > 0)
                            {
                                Builder.AppendInstruction($"add eax, {memberOffset}", $"Offset for implicit this->{varExpr.Identifier.Value}");
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'");
                    }
                    break;
                }
            case MemberAccessExpressionNode memberAccess:
                {
                    var analysisContext = CreateAnalysisContext();
                    var leftType = SemanticAnalyzer.AnalyzeExpressionType(memberAccess.Left, analysisContext);
                    string baseStructType = leftType.EndsWith("*") ? leftType.Substring(0, leftType.Length - 1) : leftType;

                    var structDef = TypeManager.FindStruct(baseStructType) ?? throw new InvalidOperationException($"Could not find struct definition for '{baseStructType}'");

                    var memberVar = structDef.Members.FirstOrDefault(m => m.Name.Value == memberAccess.Member.Value);
                    if (memberVar == null) throw new InvalidOperationException($"Struct '{baseStructType}' has no member '{memberAccess.Member.Value}'");

                    var callerStructName = CurrentFunction.OwnerStructName != null ? TypeManager.GetStructTypeFromUnqualifiedName(CurrentFunction.OwnerStructName, CurrentFunction.Namespace).FullName : null;

                    if (memberVar.AccessLevel == AccessSpecifier.Private && callerStructName != baseStructType)
                    {
                        throw new InvalidOperationException($"Cannot access private member '{baseStructType}::{memberVar.Name.Value}' from context '{callerStructName ?? "global"}'.");
                    }

                    if (memberAccess.Operator.Type == TokenType.Dot)
                    {
                        GenerateLValueAddress(memberAccess.Left);
                    }
                    else
                    {
                        GenerateExpression(memberAccess.Left);
                    }

                    var (memberOffset, _) = TypeManager.GetMemberInfo(baseStructType, memberAccess.Member.Value, CurrentCompilationUnit);
                    if (memberOffset > 0)
                    {
                        Builder.AppendInstruction($"add eax, {memberOffset}", $"Offset for member {memberAccess.Operator.Value}{memberAccess.Member.Value}");
                    }
                    break;
                }
            case UnaryExpressionNode u when u.Operator.Type == TokenType.Star:
                GenerateExpression(u.Right);
                break;
            default: throw new InvalidOperationException($"Expression '{expression.GetType().Name}' is not a valid L-value.");
        }
    }

    public void GenerateExpression(ExpressionNode expression)
    {
        switch (expression)
        {
            case IntegerLiteralNode literal: Builder.AppendInstruction($"mov eax, {literal.Value}"); break;
            case StringLiteralNode str: Builder.AppendInstruction($"mov eax, {str.Label}"); break;
            case VariableExpressionNode varExpr:
                {
                    if (CurrentSymbols.TryGetSymbol(varExpr.Identifier.Value, out int offset, out string symbolResolvedType, out _))
                    {
                        // It's a parameter or local variable
                        if (TypeManager.IsStruct(symbolResolvedType))
                        {
                            // If it's a struct, we push its address into EAX (L-value).
                            // This assumes struct values are typically used as pointers/addresses for assignments/passing.
                            string sign = offset > 0 ? "+" : ""; // offset is positive for parameters, negative for local variables
                            Builder.AppendInstruction($"lea eax, [ebp {sign} {offset}]", $"Get address of var/param {varExpr.Identifier.Value}");
                        }
                        else
                        {
                            // Primitive type or pointer
                            string sign = offset > 0 ? "+" : "";
                            if (TypeManager.GetSizeOfType(symbolResolvedType, CurrentCompilationUnit) == 1)
                                Builder.AppendInstruction($"movzx eax, byte [ebp {sign} {offset}]", $"Load value of {varExpr.Identifier.Value}");
                            else
                                Builder.AppendInstruction($"mov eax, [ebp {sign} {offset}]", $"Load value of {varExpr.Identifier.Value}");
                        }
                    }
                    else
                    {
                        // Not a local variable or parameter.
                        // Check if it's an unqualified enum member (like KEY_D)
                        var enumValue = TypeManager.ResolveUnqualifiedEnumMember(varExpr.Identifier.Value, CurrentCompilationUnit, CurrentFunction.Namespace);
                        if (enumValue.HasValue)
                        {
                            Builder.AppendInstruction($"mov eax, {enumValue.Value}", $"Enum member {varExpr.Identifier.Value}");
                        }
                        // Else if inside a method, it could be an implicit 'this->member'
                        else if (CurrentFunction.OwnerStructName != null)
                        {
                            var analysisContext = CreateAnalysisContext();
                            // Generate L-value address and then load its value
                            GenerateLValueAddress(varExpr); // This will handle the 'this' offset and member offset
                            var type = SemanticAnalyzer.AnalyzeExpressionType(varExpr, analysisContext);
                            if (TypeManager.GetSizeOfType(type, CurrentCompilationUnit) == 1) Builder.AppendInstruction("movzx eax, byte [eax]");
                            else Builder.AppendInstruction("mov eax, [eax]");
                        }
                        else
                        {
                            // Truly undefined variable
                            throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'.");
                        }
                    }
                    break;
                }
            case UnaryExpressionNode u:
                {
                    if (u.Operator.Type == TokenType.Ampersand) GenerateLValueAddress(u.Right);
                    else
                    {
                        GenerateExpression(u.Right);
                        if (u.Operator.Type == TokenType.Minus) Builder.AppendInstruction("neg eax");
                        else if (u.Operator.Type == TokenType.Star)
                        {
                            var analysisContext = CreateAnalysisContext();
                            var type = SemanticAnalyzer.AnalyzeExpressionType(u, analysisContext);
                            if (TypeManager.GetSizeOfType(type, CurrentCompilationUnit) == 1) Builder.AppendInstruction("movzx eax, byte [eax]");
                            else Builder.AppendInstruction("mov eax, [eax]");
                        }
                    }
                    break;
                }
            case MemberAccessExpressionNode m:
                {
                    var analysisContext = CreateAnalysisContext();
                    GenerateLValueAddress(m);
                    var memberType = SemanticAnalyzer.AnalyzeExpressionType(m, analysisContext);
                    if (TypeManager.GetSizeOfType(memberType, CurrentCompilationUnit) == 1) Builder.AppendInstruction("movzx eax, byte [eax]");
                    else Builder.AppendInstruction("mov eax, [eax]");
                    break;
                }
            case AssignmentExpressionNode assign:
                {
                    var analysisContext = CreateAnalysisContext();
                    bool isConstAssignment = false;
                    string? targetNameForError = null;

                    if (assign.Left is VariableExpressionNode varTarget)
                    {
                        targetNameForError = varTarget.Identifier.Value;
                        // Check if it's a local variable or parameter
                        if (CurrentSymbols.TryGetSymbol(varTarget.Identifier.Value, out _, out _, out bool isLocalConst))
                        {
                            isConstAssignment = isLocalConst;
                        }
                        else if (CurrentFunction.OwnerStructName != null)
                        {
                            // If not a local/param, and inside a method, assume it's an implicit 'this->member'
                            string ownerStructFQN = TypeManager.GetStructTypeFromUnqualifiedName(CurrentFunction.OwnerStructName, CurrentFunction.Namespace).FullName;
                            isConstAssignment = TypeManager.IsMemberConst(ownerStructFQN, varTarget.Identifier.Value);
                        }
                    }
                    else if (assign.Left is MemberAccessExpressionNode memberTarget)
                    {
                        targetNameForError = memberTarget.Member.Value;
                        // It's an explicit member access (e.g., 'obj.member = ...')
                        var ownerType = SemanticAnalyzer.AnalyzeExpressionType(memberTarget.Left, analysisContext);
                        string ownerStructFQN = ownerType.TrimEnd('*');
                        isConstAssignment = TypeManager.IsMemberConst(ownerStructFQN, memberTarget.Member.Value);
                    }

                    if (isConstAssignment)
                    {
                        throw new InvalidOperationException($"Cannot assign to constant target '{targetNameForError ?? assign.Left.ToString()}'.");
                    }

                    var lValueType = SemanticAnalyzer.AnalyzeExpressionType(assign.Left, analysisContext);
                    var isStructAssign = TypeManager.IsStruct(lValueType);

                    if (isStructAssign)
                    {
                        GenerateLValueAddress(assign.Left);
                        Builder.AppendInstruction("push eax");
                        GenerateExpression(assign.Right);
                        Builder.AppendInstruction("pop edi");
                        Builder.AppendInstruction("mov esi, eax");

                        int size = TypeManager.GetSizeOfType(lValueType, CurrentCompilationUnit);
                        Builder.AppendInstruction($"mov ecx, {size / 4}");
                        Builder.AppendInstruction("rep movsd");
                        if (size % 4 > 0)
                        {
                            Builder.AppendInstruction($"mov ecx, {size % 4}");
                            Builder.AppendInstruction("rep movsb");
                        }
                    }
                    else
                    {
                        GenerateLValueAddress(assign.Left);
                        Builder.AppendInstruction("push eax");
                        GenerateExpression(assign.Right);
                        Builder.AppendInstruction("pop ecx");
                        if (TypeManager.GetSizeOfType(lValueType, CurrentCompilationUnit) == 1) Builder.AppendInstruction("mov [ecx], al");
                        else Builder.AppendInstruction("mov [ecx], eax");
                    }
                    break;
                }
            case BinaryExpressionNode binExpr: GenerateBinaryExpression(binExpr); break;
            case CallExpressionNode callExpr: GenerateCallExpression(callExpr); break;
            case QualifiedAccessExpressionNode qNode:
                {
                    // First, try to resolve the "Namespace" part as an Enum Type Name
                    string? enumTypeFQN = TypeManager.ResolveEnumTypeName(qNode.Namespace.Value, CurrentFunction.Namespace, CurrentCompilationUnit);
                    if (enumTypeFQN != null)
                    {
                        var enumValue = TypeManager.GetEnumValue(enumTypeFQN, qNode.Member.Value);
                        if (enumValue.HasValue)
                        {
                            Builder.AppendInstruction($"mov eax, {enumValue.Value}", $"Enum member {qNode.Namespace.Value}::{qNode.Member.Value}");
                            break;
                        }
                    }

                    // If not an enum member access, it must be a qualified function call
                    var func = TypeManager.ResolveFunctionCall(qNode, CurrentCompilationUnit, CurrentFunction);

                    if (func.Body == null)
                    {
                        ExternalFunctions.Add(func.Name);
                        Builder.AppendInstruction($"mov eax, [{func.Name}]");
                    }
                    else
                    {
                        var nameParts = new List<string?> { func.Namespace, func.OwnerStructName, func.Name }.Where(n => n != null);
                        Builder.AppendInstruction($"mov eax, _{string.Join("_", nameParts)}");
                    }
                    break;
                }
            default: throw new NotImplementedException($"Expr: {expression.GetType().Name}");
        }
    }

    private void GenerateCallExpression(CallExpressionNode callExpr)
    {
        var analysisContext = CreateAnalysisContext();
        int totalArgSize = 0;
        string calleeTarget;

        foreach (var arg in callExpr.Arguments.AsEnumerable().Reverse())
        {
            var argType = SemanticAnalyzer.AnalyzeExpressionType(arg, analysisContext);
            var isStruct = TypeManager.IsStruct(argType);

            if (isStruct)
            {
                int argSize = TypeManager.GetSizeOfType(argType, CurrentCompilationUnit);
                GenerateLValueAddress(arg);
                for (int offset = argSize - 4; offset >= 0; offset -= 4)
                {
                    Builder.AppendInstruction($"push dword [eax + {offset}]");
                }
                totalArgSize += argSize;
            }
            else
            {
                GenerateExpression(arg);
                Builder.AppendInstruction("push eax");
                totalArgSize += 4;
            }
        }

        if (callExpr.Callee is MemberAccessExpressionNode memberAccess)
        {
            var leftType = SemanticAnalyzer.AnalyzeExpressionType(memberAccess.Left, analysisContext);
            var (structDef, qualifiedStructName) = TypeManager.GetStructTypeFromFullName(leftType.TrimEnd('*'));

            var method = TypeManager.ResolveMethod(structDef, memberAccess.Member.Value);

            var callerStructName = CurrentFunction.OwnerStructName != null ? TypeManager.GetStructTypeFromUnqualifiedName(CurrentFunction.OwnerStructName, CurrentFunction.Namespace).FullName : null;

            if (method.AccessLevel == AccessSpecifier.Private && callerStructName != qualifiedStructName)
                throw new InvalidOperationException($"Cannot access private method '{qualifiedStructName}::{memberAccess.Member.Value}'.");

            GenerateLValueAddress(memberAccess.Left);
            Builder.AppendInstruction("push eax", "Push 'this' pointer");
            totalArgSize += 4;

            var nameParts = new List<string?> { method.Namespace, method.OwnerStructName, method.Name }.Where(n => n != null);
            calleeTarget = "_" + string.Join("_", nameParts);
        }
        else
        {
            var func = TypeManager.ResolveFunctionCall(callExpr.Callee, CurrentCompilationUnit, CurrentFunction);

            if (func.Body == null)
            {
                ExternalFunctions.Add(func.Name);
                calleeTarget = $"[{func.Name}]";
            }
            else
            {
                var nameParts = new List<string?> { func.Namespace, func.OwnerStructName, func.Name }.Where(n => n != null);
                calleeTarget = "_" + string.Join("_", nameParts);
            }
        }

        Builder.AppendInstruction($"call {calleeTarget}");

        if (totalArgSize > 0) Builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up args");
    }

    private void GenerateBinaryExpression(BinaryExpressionNode binExpr)
    {
        GenerateExpression(binExpr.Right);
        Builder.AppendInstruction("push eax");
        GenerateExpression(binExpr.Left);
        Builder.AppendInstruction("pop ecx");

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