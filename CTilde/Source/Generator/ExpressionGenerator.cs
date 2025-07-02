using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class ExpressionGenerator
{
    private readonly CodeGenerator _context;
    private AssemblyBuilder Builder => _context.Builder;
    private TypeManager TypeManager => _context.TypeManager;
    private ProgramNode Program => _context.Program;
    private SymbolTable CurrentSymbols => _context.CurrentSymbols;
    private CompilationUnitNode CurrentCompilationUnit => _context.CurrentCompilationUnit;
    private HashSet<string> ExternalFunctions => _context.ExternalFunctions;

    public ExpressionGenerator(CodeGenerator context)
    {
        _context = context;
    }

    public void GenerateLValueAddress(ExpressionNode expression)
    {
        switch (expression)
        {
            case VariableExpressionNode varExpr:
                {
                    var function = (FunctionDeclarationNode)varExpr.Ancestors().First(a => a is FunctionDeclarationNode);
                    if (CurrentSymbols.TryGetSymbol(varExpr.Identifier.Value, out var offset, out _))
                    {
                        string sign = offset > 0 ? "+" : "";
                        Builder.AppendInstruction($"lea eax, [ebp {sign} {offset}]", $"Get address of var/param {varExpr.Identifier.Value}");
                    }
                    else if (function.OwnerStructName != null)
                    {
                        try
                        {
                            var type = TypeManager.GetStructTypeFromUnqualifiedName(function.OwnerStructName, function.Namespace);
                            var (memberOffset, _) = TypeManager.GetMemberInfo(type.FullName, varExpr.Identifier.Value, CurrentCompilationUnit);
                            CurrentSymbols.TryGetSymbol("this", out var thisOffset, out _);
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
                    var leftType = TypeManager.GetExpressionType(memberAccess.Left, CurrentSymbols, CurrentCompilationUnit);
                    string baseStructType = leftType.EndsWith("*") ? leftType.Substring(0, leftType.Length - 1) : leftType;

                    var structDef = TypeManager.FindStruct(baseStructType) ?? throw new InvalidOperationException($"Could not find struct definition for '{baseStructType}'");

                    var memberVar = structDef.Members.FirstOrDefault(m => m.Name.Value == memberAccess.Member.Value);
                    if (memberVar == null) throw new InvalidOperationException($"Struct '{baseStructType}' has no member '{memberAccess.Member.Value}'");

                    var function = (FunctionDeclarationNode)memberAccess.Ancestors().First(a => a is FunctionDeclarationNode);
                    var callerStructName = function.OwnerStructName != null ? TypeManager.GetStructTypeFromUnqualifiedName(function.OwnerStructName, function.Namespace).FullName : null;

                    if (memberVar.AccessLevel == AccessSpecifier.Private && callerStructName != baseStructType)
                    {
                        throw new InvalidOperationException($"Cannot access private member '{baseStructType}::{memberAccess.Member.Value}' from context '{callerStructName ?? "global"}'.");
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
                CurrentSymbols.TryGetSymbol(varExpr.Identifier.Value, out int offset, out _);
                if (offset > 0) // It's a parameter or local variable
                {
                    var exprType = TypeManager.GetExpressionType(varExpr, CurrentSymbols, CurrentCompilationUnit);
                    if (TypeManager.IsStruct(exprType))
                    {
                        Builder.AppendInstruction($"lea eax, [ebp + {offset}]");
                    }
                    else
                    {
                        if (TypeManager.GetSizeOfType(exprType, CurrentCompilationUnit) == 1) Builder.AppendInstruction($"movzx eax, byte [ebp + {offset}]");
                        else Builder.AppendInstruction($"mov eax, [ebp + {offset}]");
                    }
                }
                else // It's a struct member (implicit 'this')
                {
                    GenerateLValueAddress(varExpr);
                    var type = TypeManager.GetExpressionType(varExpr, CurrentSymbols, CurrentCompilationUnit);
                    if (TypeManager.GetSizeOfType(type, CurrentCompilationUnit) == 1) Builder.AppendInstruction("movzx eax, byte [eax]");
                    else Builder.AppendInstruction("mov eax, [eax]");
                }
                break;
            case UnaryExpressionNode u:
                if (u.Operator.Type == TokenType.Ampersand) GenerateLValueAddress(u.Right);
                else
                {
                    GenerateExpression(u.Right);
                    if (u.Operator.Type == TokenType.Minus) Builder.AppendInstruction("neg eax");
                    else if (u.Operator.Type == TokenType.Star)
                    {
                        var type = TypeManager.GetExpressionType(u, CurrentSymbols, CurrentCompilationUnit);
                        if (TypeManager.GetSizeOfType(type, CurrentCompilationUnit) == 1) Builder.AppendInstruction("movzx eax, byte [eax]");
                        else Builder.AppendInstruction("mov eax, [eax]");
                    }
                }
                break;
            case MemberAccessExpressionNode m:
                GenerateLValueAddress(m);
                var memberType = TypeManager.GetExpressionType(m, CurrentSymbols, CurrentCompilationUnit);
                if (TypeManager.GetSizeOfType(memberType, CurrentCompilationUnit) == 1) Builder.AppendInstruction("movzx eax, byte [eax]");
                else Builder.AppendInstruction("mov eax, [eax]");
                break;
            case AssignmentExpressionNode assign:
                {
                    var lValueType = TypeManager.GetExpressionType(assign.Left, CurrentSymbols, CurrentCompilationUnit);
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
            case QualifiedAccessExpressionNode: throw new InvalidOperationException("Qualified access expression cannot be evaluated as a value directly. It must be part of a call.");
            default: throw new NotImplementedException($"Expr: {expression.GetType().Name}");
        }
    }

    private void GenerateCallExpression(CallExpressionNode callExpr)
    {
        int totalArgSize = 0;
        string calleeTarget;

        foreach (var arg in callExpr.Arguments.AsEnumerable().Reverse())
        {
            var argType = TypeManager.GetExpressionType(arg, CurrentSymbols, CurrentCompilationUnit);
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
            var leftType = TypeManager.GetExpressionType(memberAccess.Left, CurrentSymbols, CurrentCompilationUnit);
            var (structDef, qualifiedStructName) = TypeManager.GetStructTypeFromFullName(leftType.TrimEnd('*'));

            var method = TypeManager.ResolveMethod(structDef, memberAccess.Member.Value);

            var function = (FunctionDeclarationNode)memberAccess.Ancestors().First(a => a is FunctionDeclarationNode);
            var callerStructName = function.OwnerStructName != null ? TypeManager.GetStructTypeFromUnqualifiedName(function.OwnerStructName, function.Namespace).FullName : null;

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
            var func = TypeManager.ResolveFunctionCall(callExpr.Callee, CurrentCompilationUnit);

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