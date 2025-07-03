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

    // L-Value Generation (Address-Of)
    public void GenerateLValueAddress(ExpressionNode expression, AnalysisContext context)
    {
        switch (expression)
        {
            case VariableExpressionNode varExpr:
                GenerateLValueForVariable(varExpr, context);
                break;
            case MemberAccessExpressionNode memberAccess:
                GenerateLValueForMemberAccess(memberAccess, context);
                break;
            case UnaryExpressionNode u when u.Operator.Type == TokenType.Star:
                // The address of a dereference is the value of the pointer expression itself.
                GenerateExpression(u.Right, context);
                break;
            default:
                throw new InvalidOperationException($"Expression '{expression.GetType().Name}' is not a valid L-value.");
        }
    }

    private void GenerateLValueForVariable(VariableExpressionNode varExpr, AnalysisContext context)
    {
        // Case 1: Local variable or parameter
        if (context.Symbols.TryGetSymbol(varExpr.Identifier.Value, out var offset, out _, out _))
        {
            string sign = offset > 0 ? "+" : ""; // Params are positive, locals are negative
            Builder.AppendInstruction($"lea eax, [ebp {sign} {offset}]", $"Get address of var/param {varExpr.Identifier.Value}");
            return;
        }

        // Case 2: Implicit 'this->member' inside a method
        if (context.CurrentFunction.OwnerStructName != null)
        {
            try
            {
                string ownerStructFqn = context.CurrentFunction.Namespace != null
                    ? $"{context.CurrentFunction.Namespace}::{context.CurrentFunction.OwnerStructName}"
                    : context.CurrentFunction.OwnerStructName;

                var (memberOffset, _) = TypeManager.GetMemberInfo(ownerStructFqn, varExpr.Identifier.Value, context.CompilationUnit);
                context.Symbols.TryGetSymbol("this", out var thisOffset, out _, out _);

                Builder.AppendInstruction($"mov eax, [ebp + {thisOffset}]", "Get `this` pointer value");
                if (memberOffset > 0)
                {
                    Builder.AppendInstruction($"add eax, {memberOffset}", $"Offset for implicit this->{varExpr.Identifier.Value}");
                }
                return;
            }
            catch (InvalidOperationException) { /* Fall through to error */ }
        }

        throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'");
    }

    private void GenerateLValueForMemberAccess(MemberAccessExpressionNode memberAccess, AnalysisContext context)
    {
        var leftType = SemanticAnalyzer.AnalyzeExpressionType(memberAccess.Left, context);
        string baseStructType = leftType.TrimEnd('*');

        var (memberOffset, _) = TypeManager.GetMemberInfo(baseStructType, memberAccess.Member.Value, context.CompilationUnit);

        // For `obj.member`, get the address of `obj` first.
        if (memberAccess.Operator.Type == TokenType.Dot)
        {
            GenerateLValueAddress(memberAccess.Left, context);
        }
        // For `ptr->member`, get the value of `ptr` first.
        else
        {
            GenerateExpression(memberAccess.Left, context);
        }

        if (memberOffset > 0)
        {
            Builder.AppendInstruction($"add eax, {memberOffset}", $"Offset for member {memberAccess.Operator.Value}{memberAccess.Member.Value}");
        }
    }

    // R-Value Generation (Value-Of)
    public void GenerateExpression(ExpressionNode expression, AnalysisContext context)
    {
        switch (expression)
        {
            case IntegerLiteralNode literal:
                GenerateIntegerLiteral(literal);
                break;
            case StringLiteralNode str:
                GenerateStringLiteral(str);
                break;
            case VariableExpressionNode varExpr:
                GenerateVariableExpression(varExpr, context);
                break;
            case UnaryExpressionNode u:
                GenerateUnaryExpression(u, context);
                break;
            case MemberAccessExpressionNode m:
                GenerateMemberAccessExpression(m, context);
                break;
            case AssignmentExpressionNode assign:
                GenerateAssignmentExpression(assign, context);
                break;
            case BinaryExpressionNode binExpr:
                GenerateBinaryExpression(binExpr, context);
                break;
            case CallExpressionNode callExpr:
                GenerateCallExpression(callExpr, context);
                break;
            case QualifiedAccessExpressionNode qNode:
                GenerateQualifiedAccessExpression(qNode, context);
                break;
            default:
                throw new NotImplementedException($"Expr: {expression.GetType().Name}");
        }
    }

    private void GenerateIntegerLiteral(IntegerLiteralNode literal)
    {
        Builder.AppendInstruction($"mov eax, {literal.Value}");
    }

    private void GenerateStringLiteral(StringLiteralNode str)
    {
        Builder.AppendInstruction($"mov eax, {str.Label}");
    }

    private void GenerateVariableExpression(VariableExpressionNode varExpr, AnalysisContext context)
    {
        // Case 1: Local variable or parameter
        if (context.Symbols.TryGetSymbol(varExpr.Identifier.Value, out int offset, out string symbolResolvedType, out _))
        {
            // If the variable is a struct value type (not a pointer), its "value" is its address.
            if (!symbolResolvedType.EndsWith("*") && TypeManager.IsStruct(symbolResolvedType))
            {
                GenerateLValueAddress(varExpr, context);
            }
            else
            {
                // For primitives and pointers, load the value from its memory location.
                string sign = offset > 0 ? "+" : "";
                if (TypeManager.GetSizeOfType(symbolResolvedType, context.CompilationUnit) == 1)
                    Builder.AppendInstruction($"movzx eax, byte [ebp {sign} {offset}]", $"Load value of {varExpr.Identifier.Value}");
                else
                    Builder.AppendInstruction($"mov eax, [ebp {sign} {offset}]", $"Load value of {varExpr.Identifier.Value}");
            }
            return;
        }

        // Case 2: Unqualified enum member (e.g., `KEY_D`)
        var enumValue = TypeManager.ResolveUnqualifiedEnumMember(varExpr.Identifier.Value, context.CompilationUnit, context.CurrentFunction.Namespace);
        if (enumValue.HasValue)
        {
            Builder.AppendInstruction($"mov eax, {enumValue.Value}", $"Enum member {varExpr.Identifier.Value}");
            return;
        }

        // Case 3: Implicit 'this->member'
        if (context.CurrentFunction.OwnerStructName != null)
        {
            var thisMemberAccess = new MemberAccessExpressionNode(new VariableExpressionNode(new Token(TokenType.Identifier, "this")), new Token(TokenType.Arrow, "->"), varExpr.Identifier)
            {
                Parent = varExpr.Parent
            };
            GenerateMemberAccessExpression(thisMemberAccess, context);
            return;
        }

        throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'.");
    }

    private void GenerateUnaryExpression(UnaryExpressionNode u, AnalysisContext context)
    {
        // Address-of operator
        if (u.Operator.Type == TokenType.Ampersand)
        {
            GenerateLValueAddress(u.Right, context);
            return;
        }

        // Regular value expression for the operand
        GenerateExpression(u.Right, context);

        switch (u.Operator.Type)
        {
            case TokenType.Minus:
                Builder.AppendInstruction("neg eax");
                break;
            case TokenType.Star: // Dereference
                var type = SemanticAnalyzer.AnalyzeExpressionType(u, context);
                if (TypeManager.GetSizeOfType(type, context.CompilationUnit) == 1)
                    Builder.AppendInstruction("movzx eax, byte [eax]");
                else
                    Builder.AppendInstruction("mov eax, [eax]");
                break;
        }
    }

    private void GenerateMemberAccessExpression(MemberAccessExpressionNode m, AnalysisContext context)
    {
        GenerateLValueAddress(m, context); // Get address of member
        var memberType = SemanticAnalyzer.AnalyzeExpressionType(m, context);

        // Load value from the address in EAX
        if (TypeManager.IsStruct(memberType))
        {
            // For struct members, the "value" is its address, which is already in EAX. Do nothing.
        }
        else if (TypeManager.GetSizeOfType(memberType, context.CompilationUnit) == 1)
        {
            Builder.AppendInstruction("movzx eax, byte [eax]");
        }
        else
        {
            Builder.AppendInstruction("mov eax, [eax]");
        }
    }

    private void GenerateAssignmentExpression(AssignmentExpressionNode assign, AnalysisContext context)
    {
        string lValueType = SemanticAnalyzer.AnalyzeExpressionType(assign.Left, context);
        bool isStructAssign = TypeManager.IsStruct(lValueType);

        if (isStructAssign)
        {
            // Right-hand side (source address)
            GenerateExpression(assign.Right, context);
            Builder.AppendInstruction("push eax", "Push source address");

            // Left-hand side (destination address)
            GenerateLValueAddress(assign.Left, context);
            Builder.AppendInstruction("mov edi, eax", "Pop destination into EDI"); // Dest
            Builder.AppendInstruction("pop esi", "Pop source into ESI"); // Source

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
            // Right-hand side (value)
            GenerateExpression(assign.Right, context);
            Builder.AppendInstruction("push eax", "Push value");

            // Left-hand side (address)
            GenerateLValueAddress(assign.Left, context);
            Builder.AppendInstruction("pop ecx", "Pop value into ECX");

            if (TypeManager.GetSizeOfType(lValueType, context.CompilationUnit) == 1)
                Builder.AppendInstruction("mov [eax], cl", "Assign byte");
            else
                Builder.AppendInstruction("mov [eax], ecx", "Assign dword");
        }
        // An assignment expression's value is the value that was assigned.
        // For primitives, it's already in ECX. Move to EAX. For structs, it's an address.
        if (!isStructAssign)
        {
            Builder.AppendInstruction("mov eax, ecx");
        }
    }

    private void GenerateCallExpression(CallExpressionNode callExpr, AnalysisContext context)
    {
        int totalArgSize = 0;

        // Push arguments onto the stack in reverse order
        foreach (var arg in callExpr.Arguments.AsEnumerable().Reverse())
        {
            var argType = SemanticAnalyzer.AnalyzeExpressionType(arg, context);
            var isStruct = TypeManager.IsStruct(argType);

            GenerateExpression(arg, context); // Result is address (for struct) or value (for primitive) in EAX

            if (isStruct)
            {
                int argSize = TypeManager.GetSizeOfType(argType, context.CompilationUnit);
                for (int offset = argSize - 4; offset >= 0; offset -= 4)
                {
                    Builder.AppendInstruction($"push dword [eax + {offset}]");
                }
                totalArgSize += argSize;
            }
            else
            {
                Builder.AppendInstruction("push eax");
                totalArgSize += 4;
            }
        }

        if (callExpr.Callee is MemberAccessExpressionNode memberAccess) // Method call
        {
            var ownerTypeFqn = SemanticAnalyzer.AnalyzeExpressionType(memberAccess.Left, context).TrimEnd('*');
            var (structDef, _) = TypeManager.GetStructTypeFromFullName(ownerTypeFqn);
            var method = TypeManager.ResolveMethod(structDef, memberAccess.Member.Value);

            // Generate 'this' pointer. For `ptr->member` this is the value of ptr. For `obj.member` this is the address of obj.
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
            else // Static dispatch
            {
                Builder.AppendInstruction($"call {TypeManager.Mangle(method)}");
            }
        }
        else // Global/namespaced function call
        {
            var func = TypeManager.ResolveFunctionCall(callExpr.Callee, context.CompilationUnit, context.CurrentFunction);
            string calleeTarget;
            if (func.Body == null)
            {
                ExternalFunctions.Add(func.Name);
                calleeTarget = $"[{func.Name}]";
            }
            else
            {
                calleeTarget = TypeManager.Mangle(func);
            }
            Builder.AppendInstruction($"call {calleeTarget}");
        }

        if (totalArgSize > 0)
        {
            Builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up args");
        }
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

    private string ResolveQualifier(ExpressionNode expr)
    {
        return expr switch
        {
            VariableExpressionNode v => v.Identifier.Value,
            QualifiedAccessExpressionNode q => $"{ResolveQualifier(q.Left)}::{q.Member.Value}",
            _ => throw new InvalidOperationException($"Cannot resolve qualifier from expression of type {expr.GetType().Name}")
        };
    }

    private void GenerateQualifiedAccessExpression(QualifiedAccessExpressionNode qNode, AnalysisContext context)
    {
        // A qualified access can be an enum member (which evaluates to an int)
        // or a qualified function name (which evaluates to an address).

        // First, try to resolve it as an enum member.
        string potentialEnumTypeName = ResolveQualifier(qNode.Left);
        string memberName = qNode.Member.Value;
        string? enumTypeFQN = TypeManager.ResolveEnumTypeName(potentialEnumTypeName, context.CurrentFunction.Namespace, context.CompilationUnit);
        if (enumTypeFQN != null)
        {
            var enumValue = TypeManager.GetEnumValue(enumTypeFQN, memberName);
            if (enumValue.HasValue)
            {
                Builder.AppendInstruction($"mov eax, {enumValue.Value}", $"Enum member {potentialEnumTypeName}::{memberName}");
                return;
            }
        }

        // If it's not an enum, it must be a function name.
        var func = TypeManager.ResolveFunctionCall(qNode, context.CompilationUnit, context.CurrentFunction);
        string calleeTarget;
        if (func.Body == null)
        {
            ExternalFunctions.Add(func.Name);
            calleeTarget = $"[{func.Name}]";
        }
        else
        {
            calleeTarget = TypeManager.Mangle(func);
        }
        Builder.AppendInstruction($"mov eax, {calleeTarget}");
    }
}