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

    // L-Value Generation (Address-Of)
    public void GenerateLValueAddress(ExpressionNode expression)
    {
        switch (expression)
        {
            case VariableExpressionNode varExpr:
                GenerateLValueForVariable(varExpr);
                break;
            case MemberAccessExpressionNode memberAccess:
                GenerateLValueForMemberAccess(memberAccess);
                break;
            case UnaryExpressionNode u when u.Operator.Type == TokenType.Star:
                // The address of a dereference is the value of the pointer expression itself.
                GenerateExpression(u.Right);
                break;
            default:
                throw new InvalidOperationException($"Expression '{expression.GetType().Name}' is not a valid L-value.");
        }
    }

    private void GenerateLValueForVariable(VariableExpressionNode varExpr)
    {
        // Case 1: Local variable or parameter
        if (CurrentSymbols.TryGetSymbol(varExpr.Identifier.Value, out var offset, out _, out _))
        {
            string sign = offset > 0 ? "+" : ""; // Params are positive, locals are negative
            Builder.AppendInstruction($"lea eax, [ebp {sign} {offset}]", $"Get address of var/param {varExpr.Identifier.Value}");
            return;
        }

        // Case 2: Implicit 'this->member' inside a method
        if (CurrentFunction.OwnerStructName != null)
        {
            try
            {
                var (Def, FullName) = TypeManager.GetStructTypeFromUnqualifiedName(CurrentFunction.OwnerStructName, CurrentFunction.Namespace);
                var (memberOffset, _) = TypeManager.GetMemberInfo(FullName, varExpr.Identifier.Value, CurrentCompilationUnit);
                CurrentSymbols.TryGetSymbol("this", out var thisOffset, out _, out _);

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

    private void GenerateLValueForMemberAccess(MemberAccessExpressionNode memberAccess)
    {
        var analysisContext = CreateAnalysisContext();
        var leftType = SemanticAnalyzer.AnalyzeExpressionType(memberAccess.Left, analysisContext);
        string baseStructType = leftType.TrimEnd('*');

        var (memberOffset, _) = TypeManager.GetMemberInfo(baseStructType, memberAccess.Member.Value, CurrentCompilationUnit);

        // For `obj.member`, get the address of `obj` first.
        if (memberAccess.Operator.Type == TokenType.Dot)
        {
            GenerateLValueAddress(memberAccess.Left);
        }
        // For `ptr->member`, get the value of `ptr` first.
        else
        {
            GenerateExpression(memberAccess.Left);
        }

        if (memberOffset > 0)
        {
            Builder.AppendInstruction($"add eax, {memberOffset}", $"Offset for member {memberAccess.Operator.Value}{memberAccess.Member.Value}");
        }
    }

    // R-Value Generation (Value-Of)
    public void GenerateExpression(ExpressionNode expression)
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
                GenerateVariableExpression(varExpr);
                break;
            case UnaryExpressionNode u:
                GenerateUnaryExpression(u);
                break;
            case MemberAccessExpressionNode m:
                GenerateMemberAccessExpression(m);
                break;
            case AssignmentExpressionNode assign:
                GenerateAssignmentExpression(assign);
                break;
            case BinaryExpressionNode binExpr:
                GenerateBinaryExpression(binExpr);
                break;
            case CallExpressionNode callExpr:
                GenerateCallExpression(callExpr);
                break;
            case QualifiedAccessExpressionNode qNode:
                GenerateQualifiedAccessExpression(qNode);
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

    private void GenerateVariableExpression(VariableExpressionNode varExpr)
    {
        // Case 1: Local variable or parameter
        if (CurrentSymbols.TryGetSymbol(varExpr.Identifier.Value, out int offset, out string symbolResolvedType, out _))
        {
            if (TypeManager.IsStruct(symbolResolvedType))
            {
                // For structs, "value" is its address (L-Value)
                GenerateLValueAddress(varExpr);
            }
            else
            {
                // For primitives/pointers, load the value from memory
                string sign = offset > 0 ? "+" : "";
                if (TypeManager.GetSizeOfType(symbolResolvedType, CurrentCompilationUnit) == 1)
                    Builder.AppendInstruction($"movzx eax, byte [ebp {sign} {offset}]", $"Load value of {varExpr.Identifier.Value}");
                else
                    Builder.AppendInstruction($"mov eax, [ebp {sign} {offset}]", $"Load value of {varExpr.Identifier.Value}");
            }
            return;
        }

        // Case 2: Unqualified enum member (e.g., `KEY_D`)
        var enumValue = TypeManager.ResolveUnqualifiedEnumMember(varExpr.Identifier.Value, CurrentCompilationUnit, CurrentFunction.Namespace);
        if (enumValue.HasValue)
        {
            Builder.AppendInstruction($"mov eax, {enumValue.Value}", $"Enum member {varExpr.Identifier.Value}");
            return;
        }

        // Case 3: Implicit 'this->member'
        if (CurrentFunction.OwnerStructName != null)
        {
            GenerateMemberAccessExpression(new MemberAccessExpressionNode(new VariableExpressionNode(new Token(TokenType.Identifier, "this")), new Token(TokenType.Arrow, "->"), varExpr.Identifier));
            return;
        }

        throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'.");
    }

    private void GenerateUnaryExpression(UnaryExpressionNode u)
    {
        // Address-of operator
        if (u.Operator.Type == TokenType.Ampersand)
        {
            GenerateLValueAddress(u.Right);
            return;
        }

        // Regular value expression for the operand
        GenerateExpression(u.Right);

        switch (u.Operator.Type)
        {
            case TokenType.Minus:
                Builder.AppendInstruction("neg eax");
                break;
            case TokenType.Star: // Dereference
                var analysisContext = CreateAnalysisContext();
                var type = SemanticAnalyzer.AnalyzeExpressionType(u, analysisContext);
                if (TypeManager.GetSizeOfType(type, CurrentCompilationUnit) == 1)
                    Builder.AppendInstruction("movzx eax, byte [eax]");
                else
                    Builder.AppendInstruction("mov eax, [eax]");
                break;
        }
    }

    private void GenerateMemberAccessExpression(MemberAccessExpressionNode m)
    {
        var analysisContext = CreateAnalysisContext();
        GenerateLValueAddress(m); // Get address of member
        var memberType = SemanticAnalyzer.AnalyzeExpressionType(m, analysisContext);

        // Load value from the address in EAX
        if (TypeManager.IsStruct(memberType))
        {
            // For struct members, the "value" is its address, which is already in EAX. Do nothing.
        }
        else if (TypeManager.GetSizeOfType(memberType, CurrentCompilationUnit) == 1)
        {
            Builder.AppendInstruction("movzx eax, byte [eax]");
        }
        else
        {
            Builder.AppendInstruction("mov eax, [eax]");
        }
    }

    private void GenerateAssignmentExpression(AssignmentExpressionNode assign)
    {
        var analysisContext = CreateAnalysisContext();
        string lValueType = SemanticAnalyzer.AnalyzeExpressionType(assign.Left, analysisContext);
        bool isStructAssign = TypeManager.IsStruct(lValueType);

        if (isStructAssign)
        {
            // Right-hand side (source address)
            GenerateExpression(assign.Right);
            Builder.AppendInstruction("push eax", "Push source address");

            // Left-hand side (destination address)
            GenerateLValueAddress(assign.Left);
            Builder.AppendInstruction("mov edi, eax", "Pop destination into EDI"); // Dest
            Builder.AppendInstruction("pop esi", "Pop source into ESI"); // Source

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
            // Right-hand side (value)
            GenerateExpression(assign.Right);
            Builder.AppendInstruction("push eax", "Push value");

            // Left-hand side (address)
            GenerateLValueAddress(assign.Left);
            Builder.AppendInstruction("pop ecx", "Pop value into ECX");

            if (TypeManager.GetSizeOfType(lValueType, CurrentCompilationUnit) == 1)
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

    private void GenerateCallExpression(CallExpressionNode callExpr)
    {
        var analysisContext = CreateAnalysisContext();
        int totalArgSize = 0;

        // Push arguments onto the stack in reverse order
        foreach (var arg in callExpr.Arguments.AsEnumerable().Reverse())
        {
            var argType = SemanticAnalyzer.AnalyzeExpressionType(arg, analysisContext);
            var isStruct = TypeManager.IsStruct(argType);

            GenerateExpression(arg); // Result is address (for struct) or value (for primitive) in EAX

            if (isStruct)
            {
                int argSize = TypeManager.GetSizeOfType(argType, CurrentCompilationUnit);
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

        string calleeTarget;
        if (callExpr.Callee is MemberAccessExpressionNode memberAccess) // Method call
        {
            var leftType = SemanticAnalyzer.AnalyzeExpressionType(memberAccess.Left, analysisContext);
            var (structDef, _) = TypeManager.GetStructTypeFromFullName(leftType.TrimEnd('*'));
            var method = TypeManager.ResolveMethod(structDef, memberAccess.Member.Value);

            // Push `this` pointer
            GenerateLValueAddress(memberAccess.Left);
            Builder.AppendInstruction("push eax", "Push 'this' pointer");
            totalArgSize += 4;

            var nameParts = new List<string?> { method.Namespace, method.OwnerStructName, method.Name }.Where(n => n != null);
            calleeTarget = "_" + string.Join("_", nameParts);
        }
        else // Global/namespaced function call
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

        if (totalArgSize > 0)
        {
            Builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up args");
        }
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

    private void GenerateQualifiedAccessExpression(QualifiedAccessExpressionNode qNode)
    {
        // Case 1: Enum member access (e.g. `rl::KEY_D`)
        string? enumTypeFQN = TypeManager.ResolveEnumTypeName(qNode.Namespace.Value, CurrentFunction.Namespace, CurrentCompilationUnit);
        if (enumTypeFQN != null)
        {
            var enumValue = TypeManager.GetEnumValue(enumTypeFQN, qNode.Member.Value);
            if (enumValue.HasValue)
            {
                Builder.AppendInstruction($"mov eax, {enumValue.Value}", $"Enum member {qNode.Namespace.Value}::{qNode.Member.Value}");
                return;
            }
        }

        // Case 2: Qualified function name (e.g. `rl::DrawText`)
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
    }
}