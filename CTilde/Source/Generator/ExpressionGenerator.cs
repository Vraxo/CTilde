using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class ExpressionGenerator
{
    private readonly CodeGenerator _context;
    private AssemblyBuilder Builder => _context.Builder;
    private TypeRepository TypeRepository => _context.TypeRepository;
    private TypeResolver TypeResolver => _context.TypeResolver;
    private FunctionResolver FunctionResolver => _context.FunctionResolver;
    private VTableManager VTableManager => _context.VTableManager;
    private MemoryLayoutManager MemoryLayoutManager => _context.MemoryLayoutManager;
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

        if (TypeRepository.IsStruct(argType) && !argType.EndsWith("*"))
        {
            int argSize = MemoryLayoutManager.GetSizeOfType(argType, context.CompilationUnit);
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
                var (memberOffset, _) = MemoryLayoutManager.GetMemberInfo(ownerStructFqn, varExpr.Identifier.Value, context.CompilationUnit);
                context.Symbols.TryGetSymbol("this", out var thisOffset, out _, out _);
                Builder.AppendInstruction($"mov eax, [ebp + {thisOffset}]", "Get `this` pointer value");
                if (memberOffset > 0) Builder.AppendInstruction($"add eax, {memberOffset}", $"Offset for implicit this->{varExpr.Identifier.Value}");
                return;
            }
            catch (InvalidOperationException) { /* Fall through */ }
        }
        throw new InvalidOperationException($"Undefined variable '{varExpr.Identifier.Value}'.");
    }

    private void GenerateLValueForMemberAccess(MemberAccessExpressionNode memberAccess, AnalysisContext context)
    {
        var leftType = SemanticAnalyzer.AnalyzeExpressionType(memberAccess.Left, context);
        string baseStructType = leftType.TrimEnd('*');
        var (memberOffset, _) = MemoryLayoutManager.GetMemberInfo(baseStructType, memberAccess.Member.Value, context.CompilationUnit);

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
            case NewExpressionNode n: GenerateNewExpression(n, context); break;
            default: throw new NotImplementedException($"Expr: {expression.GetType().Name}");
        }
    }

    private void GenerateNewExpression(NewExpressionNode n, AnalysisContext context)
    {
        var typeFqn = TypeResolver.ResolveTypeName(n.Type.Value, context.CurrentFunction.Namespace, context.CompilationUnit);
        var size = MemoryLayoutManager.GetSizeOfType(typeFqn, context.CompilationUnit);

        Builder.AppendInstruction($"push {size}", "Push size for malloc");
        Builder.AppendInstruction("call [malloc]");
        Builder.AppendInstruction("add esp, 4", "Clean up malloc arg");
        Builder.AppendInstruction("mov edi, eax", "Save new'd pointer in edi");

        var argTypes = n.Arguments.Select(arg => SemanticAnalyzer.AnalyzeExpressionType(arg, context)).ToList();
        var ctor = FunctionResolver.FindConstructor(typeFqn, argTypes) ?? throw new InvalidOperationException($"No matching constructor for 'new {typeFqn}'");

        if (VTableManager.HasVTable(typeFqn))
        {
            var structDef = TypeRepository.FindStruct(typeFqn);
            var vtableLabel = NameMangler.GetVTableLabel(structDef);
            Builder.AppendInstruction($"mov dword [edi], {vtableLabel}", "Set vtable pointer on heap object");
        }

        int totalArgSize = 0;
        foreach (var arg in n.Arguments.AsEnumerable().Reverse())
        {
            totalArgSize += PushArgument(arg, context);
        }

        Builder.AppendInstruction("push edi", "Push 'this' pointer for constructor");
        totalArgSize += 4;

        Builder.AppendInstruction($"call {NameMangler.Mangle(ctor)}");
        Builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up ctor args");

        Builder.AppendInstruction("mov eax, edi", "Return pointer to new object in eax");
    }

    private void GenerateIntegerLiteral(IntegerLiteralNode literal) => Builder.AppendInstruction($"mov eax, {literal.Value}");
    private void GenerateStringLiteral(StringLiteralNode str) => Builder.AppendInstruction($"mov eax, {str.Label}");

    private void GenerateVariableExpression(VariableExpressionNode varExpr, AnalysisContext context)
    {
        if (context.Symbols.TryGetSymbol(varExpr.Identifier.Value, out int offset, out string type, out _))
        {
            if (TypeRepository.IsStruct(type) && !type.EndsWith("*")) GenerateLValueAddress(varExpr, context);
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

        if (context.CurrentFunction?.OwnerStructName != null)
        {
            var thisMemberAccess = new MemberAccessExpressionNode(new VariableExpressionNode(new Token(TokenType.Identifier, "this", -1, -1)), new Token(TokenType.Arrow, "->", -1, -1), varExpr.Identifier) { Parent = varExpr.Parent };
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
                string instruction = MemoryLayoutManager.GetSizeOfType(type, context.CompilationUnit) == 1 ? "movzx eax, byte [eax]" : "mov eax, [eax]";
                Builder.AppendInstruction(instruction);
                break;
        }
    }

    private void GenerateMemberAccessExpression(MemberAccessExpressionNode m, AnalysisContext context)
    {
        GenerateLValueAddress(m, context);
        var memberType = SemanticAnalyzer.AnalyzeExpressionType(m, context);
        if (TypeRepository.IsStruct(memberType) && !memberType.EndsWith("*")) return;

        string instruction = MemoryLayoutManager.GetSizeOfType(memberType, context.CompilationUnit) == 1 ? "movzx eax, byte [eax]" : "mov eax, [eax]";
        Builder.AppendInstruction(instruction);
    }

    private void GenerateAssignmentExpression(AssignmentExpressionNode assign, AnalysisContext context)
    {
        string lValueType = SemanticAnalyzer.AnalyzeExpressionType(assign.Left, context);
        bool isStructAssign = TypeRepository.IsStruct(lValueType);

        if (isStructAssign)
        {
            GenerateExpression(assign.Right, context);
            Builder.AppendInstruction("push eax", "Push source address");
            GenerateLValueAddress(assign.Left, context);
            Builder.AppendInstruction("mov edi, eax", "Pop destination into EDI");
            Builder.AppendInstruction("pop esi", "Pop source into ESI");
            int size = MemoryLayoutManager.GetSizeOfType(lValueType, context.CompilationUnit);
            Builder.AppendInstruction($"push {size}");
            Builder.AppendInstruction("push esi");
            Builder.AppendInstruction("push edi");
            Builder.AppendInstruction("call [memcpy]");
            Builder.AppendInstruction("add esp, 12");
        }
        else
        {
            GenerateExpression(assign.Right, context);
            Builder.AppendInstruction("push eax", "Push value");
            GenerateLValueAddress(assign.Left, context);
            Builder.AppendInstruction("pop ecx", "Pop value into ECX");
            string instruction = MemoryLayoutManager.GetSizeOfType(lValueType, context.CompilationUnit) == 1 ? "mov [eax], cl" : "mov [eax], ecx";
            Builder.AppendInstruction(instruction, "Assign value");
        }
        if (!isStructAssign) Builder.AppendInstruction("mov eax, ecx");
    }

    private void GenerateCallExpression(CallExpressionNode callExpr, AnalysisContext context)
    {
        int totalArgSize = 0;
        FunctionDeclarationNode func;

        if (callExpr.Callee is MemberAccessExpressionNode memberAccess)
        {
            var ownerTypeName = SemanticAnalyzer.AnalyzeExpressionType(memberAccess.Left, context).TrimEnd('*');
            var ownerStruct = TypeRepository.FindStruct(ownerTypeName) ?? throw new InvalidOperationException($"Could not find struct definition for '{ownerTypeName}'.");
            func = FunctionResolver.ResolveMethod(ownerStruct, memberAccess.Member.Value);
        }
        else
        {
            func = FunctionResolver.ResolveFunctionCall(callExpr.Callee, context.CompilationUnit, context.CurrentFunction);
        }

        var returnType = SemanticAnalyzer.AnalyzeFunctionReturnType(func, context);
        bool returnsStructByValue = TypeRepository.IsStruct(returnType) && !returnType.EndsWith("*");

        if (returnsStructByValue)
        {
            var size = MemoryLayoutManager.GetSizeOfType(returnType, context.CompilationUnit);
            Builder.AppendInstruction($"sub esp, {size}", "Make space for return value");
            Builder.AppendInstruction("push esp", "Push hidden return value pointer");
            totalArgSize += 4;
        }

        foreach (var arg in callExpr.Arguments.AsEnumerable().Reverse())
        {
            totalArgSize += PushArgument(arg, context);
        }

        if (callExpr.Callee is MemberAccessExpressionNode ma)
        {
            if (ma.Operator.Type == TokenType.Arrow) GenerateExpression(ma.Left, context);
            else GenerateLValueAddress(ma.Left, context);

            Builder.AppendInstruction("push eax", "Push 'this' pointer");
            totalArgSize += 4;

            if (func.IsVirtual || func.IsOverride)
            {
                var ownerTypeFqn = SemanticAnalyzer.AnalyzeExpressionType(ma.Left, context).TrimEnd('*');
                var vtableIndex = VTableManager.GetMethodVTableIndex(ownerTypeFqn, func.Name);
                int thisPtrOffset = totalArgSize - 4; // 'this' is the last thing pushed before call
                Builder.AppendInstruction($"mov eax, [esp + {thisPtrOffset}]", "Get 'this' from stack");
                Builder.AppendInstruction("mov eax, [eax]", "Get vtable pointer from object");
                Builder.AppendInstruction($"mov eax, [eax + {vtableIndex * 4}]", $"Get method address from vtable[{vtableIndex}]");
                Builder.AppendInstruction("call eax");
            }
            else { Builder.AppendInstruction($"call {NameMangler.Mangle(func)}"); }
        }
        else
        {
            string calleeTarget = func.Body == null ? $"[{func.Name}]" : NameMangler.Mangle(func);
            if (func.Body == null) ExternalFunctions.Add(func.Name);
            Builder.AppendInstruction($"call {calleeTarget}");
        }

        Builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up args");

        if (returnsStructByValue)
        {
            Builder.AppendInstruction("lea eax, [esp]", "Get address of hidden return temporary");
        }
    }

    private void GenerateBinaryExpression(BinaryExpressionNode binExpr, AnalysisContext context)
    {
        var leftTypeFqn = SemanticAnalyzer.AnalyzeExpressionType(binExpr.Left, context);

        if (TypeRepository.IsStruct(leftTypeFqn))
        {
            var opName = $"operator_{NameMangler.MangleOperator(binExpr.Operator.Value)}";
            var overload = FunctionResolver.FindMethod(leftTypeFqn.TrimEnd('*'), opName) ?? throw new InvalidOperationException($"Internal compiler error: overload for '{opName}' not found.");

            var returnType = SemanticAnalyzer.AnalyzeFunctionReturnType(overload, context);
            bool returnsStructByValue = TypeRepository.IsStruct(returnType) && !returnType.EndsWith("*");
            int totalArgSize = 0;

            if (returnsStructByValue)
            {
                var size = MemoryLayoutManager.GetSizeOfType(returnType, context.CompilationUnit);
                Builder.AppendInstruction($"sub esp, {size}", "Make space for op+ return value");
                Builder.AppendInstruction("push esp", "Push hidden return value pointer");
                totalArgSize += 4;
            }

            totalArgSize += PushArgument(binExpr.Right, context);

            GenerateLValueAddress(binExpr.Left, context);
            Builder.AppendInstruction("push eax", "Push 'this' pointer");
            totalArgSize += 4;

            Builder.AppendInstruction($"call {NameMangler.Mangle(overload)}");
            Builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up op+ args");

            if (returnsStructByValue)
            {
                Builder.AppendInstruction("lea eax, [esp]", "Get address of hidden return temporary");
            }
        }
        else
        {
            GenerateExpression(binExpr.Left, context);
            Builder.AppendInstruction("push eax");
            GenerateExpression(binExpr.Right, context);
            Builder.AppendInstruction("mov ecx, eax");
            Builder.AppendInstruction("pop eax");

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

    private void GenerateQualifiedAccessExpression(QualifiedAccessExpressionNode qNode, AnalysisContext context)
    {
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

        var func = FunctionResolver.ResolveFunctionCall(qNode, context.CompilationUnit, context.CurrentFunction);
        string calleeTarget = func.Body == null ? $"[{func.Name}]" : NameMangler.Mangle(func);
        if (func.Body == null) ExternalFunctions.Add(func.Name);
        Builder.AppendInstruction($"mov eax, {calleeTarget}");
    }
}