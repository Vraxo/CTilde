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
    private HashSet<string> ExternalFunctions => _context.ExternalFunctions;

    public ExpressionGenerator(CodeGenerator context)
    {
        _context = context;
    }

    public int PushArgument(ExpressionNode arg, AnalysisContext context)
    {
        var argType = SemanticAnalyzer.AnalyzeExpressionType(arg, context);
        GenerateExpression(arg, context); // Result is address (for struct) or value (for primitive) in EAX

        if (TypeManager.IsStruct(argType))
        {
            int argSize = TypeManager.GetSizeOfType(argType, context.CompilationUnit);
            for (int offset = argSize - 4; offset >= 0; offset -= 4)
            {
                Builder.AppendInstruction($"push dword [eax + {offset}]");
            }
            return argSize;
        }
        else
        {
            Builder.AppendInstruction("push eax");
            return 4;
        }
    }

    public void GenerateLValueAddress(ExpressionNode expression, AnalysisContext context)
    {
        switch (expression)
        {
            case VariableExpressionNode varExpr: GenerateLValueForVariable(varExpr, context); break;
            case MemberAccessExpressionNode memberAccess: GenerateLValueForMemberAccess(memberAccess, context); break;
            case UnaryExpressionNode u when u.Operator.Type == TokenType.Star: GenerateExpression(u.Right, context); break;
            default: throw new InvalidOperationException($"Expression '{expression.GetType().Name}' is not a valid L-value.");
        }
    }

    private void GenerateLValueForVariable(VariableExpressionNode varExpr, AnalysisContext context)
    {
        if (context.Symbols.TryGetSymbol(varExpr.Identifier.Value, out var offset, out _, out _))
        {
            string sign = offset > 0 ? "+" : "";
            Builder.AppendInstruction($"lea eax, [ebp {sign} {offset}]", $"Get address of var/param {varExpr.Identifier.Value}");
            return;
        }

        if (context.CurrentFunction?.OwnerStructName != null)
        {
            try
            {
                string ownerStructFqn = context.CurrentFunction.Namespace != null ? $"{context.CurrentFunction.Namespace}::{context.CurrentFunction.OwnerStructName}" : context.CurrentFunction.OwnerStructName;
                var (memberOffset, _) = TypeManager.GetMemberInfo(ownerStructFqn, varExpr.Identifier.Value, context.CompilationUnit);
                context.Symbols.TryGetSymbol("this", out var thisOffset, out _, out _);
                Builder.AppendInstruction($"mov eax, [ebp + {thisOffset}]", "Get `this` pointer value");
                if (memberOffset > 0) Builder.AppendInstruction($"add eax, {memberOffset}", $"Offset for implicit this->{varExpr.Identifier.Value}");
                return;
            }
            catch (InvalidOperationException) { /* Fall through */ }
        }
        throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'");
    }

    private void GenerateLValueForMemberAccess(MemberAccessExpressionNode memberAccess, AnalysisContext context)
    {
        var leftType = SemanticAnalyzer.AnalyzeExpressionType(memberAccess.Left, context);
        string baseStructType = leftType.TrimEnd('*');
        var (memberOffset, _) = TypeManager.GetMemberInfo(baseStructType, memberAccess.Member.Value, context.CompilationUnit);

        if (memberAccess.Operator.Type == TokenType.Dot) GenerateLValueAddress(memberAccess.Left, context);
        else GenerateExpression(memberAccess.Left, context);

        if (memberOffset > 0) Builder.AppendInstruction($"add eax, {memberOffset}", $"Offset for member {memberAccess.Operator.Value}{memberAccess.Member.Value}");
    }

    public void GenerateExpression(ExpressionNode expression, AnalysisContext context)
    {
        switch (expression)
        {
            case IntegerLiteralNode literal: GenerateIntegerLiteral(literal); break;
            case StringLiteralNode str: GenerateStringLiteral(str); break;
            case VariableExpressionNode varExpr: GenerateVariableExpression(varExpr, context); break;
            case UnaryExpressionNode u: GenerateUnaryExpression(u, context); break;
            case MemberAccessExpressionNode m: GenerateMemberAccessExpression(m, context); break;
            case AssignmentExpressionNode assign: GenerateAssignmentExpression(assign, context); break;
            case BinaryExpressionNode binExpr: GenerateBinaryExpression(binExpr, context); break;
            case CallExpressionNode callExpr: GenerateCallExpression(callExpr, context); break;
            case QualifiedAccessExpressionNode qNode: GenerateQualifiedAccessExpression(qNode, context); break;
            default: throw new NotImplementedException($"Expr: {expression.GetType().Name}");
        }
    }

    private void GenerateIntegerLiteral(IntegerLiteralNode literal) => Builder.AppendInstruction($"mov eax, {literal.Value}");
    private void GenerateStringLiteral(StringLiteralNode str) => Builder.AppendInstruction($"mov eax, {str.Label}");

    private void GenerateVariableExpression(VariableExpressionNode varExpr, AnalysisContext context)
    {
        if (context.Symbols.TryGetSymbol(varExpr.Identifier.Value, out int offset, out string type, out _))
        {
            if (!type.EndsWith("*") && TypeManager.IsStruct(type)) GenerateLValueAddress(varExpr, context);
            else
            {
                string sign = offset > 0 ? "+" : "";
                string instruction = TypeManager.GetSizeOfType(type, context.CompilationUnit) == 1 ? "movzx eax, byte" : "mov eax,";
                Builder.AppendInstruction($"{instruction} [ebp {sign} {offset}]", $"Load value of {varExpr.Identifier.Value}");
            }
            return;
        }

        var enumValue = TypeManager.ResolveUnqualifiedEnumMember(varExpr.Identifier.Value, context.CompilationUnit, context.CurrentFunction?.Namespace);
        if (enumValue.HasValue)
        {
            Builder.AppendInstruction($"mov eax, {enumValue.Value}", $"Enum member {varExpr.Identifier.Value}");
            return;
        }

        if (context.CurrentFunction?.OwnerStructName != null)
        {
            var thisMemberAccess = new MemberAccessExpressionNode(new VariableExpressionNode(new Token(TokenType.Identifier, "this")), new Token(TokenType.Arrow, "->"), varExpr.Identifier) { Parent = varExpr.Parent };
            GenerateMemberAccessExpression(thisMemberAccess, context);
            return;
        }
        throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'.");
    }

    private void GenerateUnaryExpression(UnaryExpressionNode u, AnalysisContext context)
    {
        if (u.Operator.Type == TokenType.Ampersand)
        {
            GenerateLValueAddress(u.Right, context);
            return;
        }
        GenerateExpression(u.Right, context);
        switch (u.Operator.Type)
        {
            case TokenType.Minus: Builder.AppendInstruction("neg eax"); break;
            case TokenType.Star:
                var type = SemanticAnalyzer.AnalyzeExpressionType(u, context);
                string instruction = TypeManager.GetSizeOfType(type, context.CompilationUnit) == 1 ? "movzx eax, byte [eax]" : "mov eax, [eax]";
                Builder.AppendInstruction(instruction);
                break;
        }
    }

    private void GenerateMemberAccessExpression(MemberAccessExpressionNode m, AnalysisContext context)
    {
        GenerateLValueAddress(m, context);
        var memberType = SemanticAnalyzer.AnalyzeExpressionType(m, context);
        if (TypeManager.IsStruct(memberType)) return;

        string instruction = TypeManager.GetSizeOfType(memberType, context.CompilationUnit) == 1 ? "movzx eax, byte [eax]" : "mov eax, [eax]";
        Builder.AppendInstruction(instruction);
    }

    private void GenerateAssignmentExpression(AssignmentExpressionNode assign, AnalysisContext context)
    {
        string lValueType = SemanticAnalyzer.AnalyzeExpressionType(assign.Left, context);
        bool isStructAssign = TypeManager.IsStruct(lValueType);

        if (isStructAssign)
        {
            GenerateExpression(assign.Right, context);
            Builder.AppendInstruction("push eax", "Push source address");
            GenerateLValueAddress(assign.Left, context);
            Builder.AppendInstruction("mov edi, eax", "Pop destination into EDI");
            Builder.AppendInstruction("pop esi", "Pop source into ESI");
            int size = TypeManager.GetSizeOfType(lValueType, context.CompilationUnit);
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
            GenerateExpression(assign.Right, context);
            Builder.AppendInstruction("push eax", "Push value");
            GenerateLValueAddress(assign.Left, context);
            Builder.AppendInstruction("pop ecx", "Pop value into ECX");
            string instruction = TypeManager.GetSizeOfType(lValueType, context.CompilationUnit) == 1 ? "mov [eax], cl" : "mov [eax], ecx";
            Builder.AppendInstruction(instruction, "Assign value");
        }
        if (!isStructAssign) Builder.AppendInstruction("mov eax, ecx");
    }

    private void GenerateCallExpression(CallExpressionNode callExpr, AnalysisContext context)
    {
        int totalArgSize = 0;
        foreach (var arg in callExpr.Arguments.AsEnumerable().Reverse())
        {
            totalArgSize += PushArgument(arg, context);
        }

        if (callExpr.Callee is MemberAccessExpressionNode memberAccess)
        {
            var ownerTypeFqn = SemanticAnalyzer.AnalyzeExpressionType(memberAccess.Left, context).TrimEnd('*');
            var (structDef, _) = TypeManager.GetStructTypeFromFullName(ownerTypeFqn);
            var method = TypeManager.ResolveMethod(structDef, memberAccess.Member.Value);

            if (memberAccess.Operator.Type == TokenType.Arrow) GenerateExpression(memberAccess.Left, context);
            else GenerateLValueAddress(memberAccess.Left, context);

            Builder.AppendInstruction("push eax", "Push 'this' pointer");
            totalArgSize += 4;

            if (method.IsVirtual || method.IsOverride)
            {
                var vtableIndex = TypeManager.GetMethodVTableIndex(ownerTypeFqn, method.Name);
                Builder.AppendInstruction("mov eax, [esp]", "Get 'this' from stack without popping");
                Builder.AppendInstruction("mov eax, [eax]", "Get vtable pointer from object");
                Builder.AppendInstruction($"mov eax, [eax + {vtableIndex * 4}]", $"Get method address from vtable[{vtableIndex}]");
                Builder.AppendInstruction("call eax");
            }
            else { Builder.AppendInstruction($"call {TypeManager.Mangle(method)}"); }
        }
        else
        {
            var func = TypeManager.ResolveFunctionCall(callExpr.Callee, context.CompilationUnit, context.CurrentFunction);
            string calleeTarget = func.Body == null ? $"[{func.Name}]" : TypeManager.Mangle(func);
            if (func.Body == null) ExternalFunctions.Add(func.Name);
            Builder.AppendInstruction($"call {calleeTarget}");
        }
        if (totalArgSize > 0) Builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up args");
    }

    private void GenerateBinaryExpression(BinaryExpressionNode binExpr, AnalysisContext context)
    {
        GenerateExpression(binExpr.Right, context);
        Builder.AppendInstruction("push eax");
        GenerateExpression(binExpr.Left, context);
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

    private string ResolveQualifier(ExpressionNode expr) => expr switch
    {
        VariableExpressionNode v => v.Identifier.Value,
        QualifiedAccessExpressionNode q => $"{ResolveQualifier(q.Left)}::{q.Member.Value}",
        _ => throw new InvalidOperationException($"Cannot resolve qualifier from expression of type {expr.GetType().Name}")
    };

    private void GenerateQualifiedAccessExpression(QualifiedAccessExpressionNode qNode, AnalysisContext context)
    {
        string potentialEnumTypeName = ResolveQualifier(qNode.Left);
        string memberName = qNode.Member.Value;
        string? enumTypeFQN = TypeManager.ResolveEnumTypeName(potentialEnumTypeName, context.CurrentFunction?.Namespace, context.CompilationUnit);
        if (enumTypeFQN != null)
        {
            var enumValue = TypeManager.GetEnumValue(enumTypeFQN, memberName);
            if (enumValue.HasValue)
            {
                Builder.AppendInstruction($"mov eax, {enumValue.Value}", $"Enum member {potentialEnumTypeName}::{memberName}");
                return;
            }
        }

        var func = TypeManager.ResolveFunctionCall(qNode, context.CompilationUnit, context.CurrentFunction);
        string calleeTarget = func.Body == null ? $"[{func.Name}]" : TypeManager.Mangle(func);
        if (func.Body == null) ExternalFunctions.Add(func.Name);
        Builder.AppendInstruction($"mov eax, {calleeTarget}");
    }
}