using System.Linq;
using CTilde.Generator;

namespace CTilde.Generator.ExpressionHandlers;

public class CallExpressionHandler : ExpressionHandlerBase
{
    public CallExpressionHandler(CodeGenerator codeGenerator) : base(codeGenerator) { }

    public override void Generate(ExpressionNode expression, AnalysisContext context)
    {
        var callExpr = (CallExpressionNode)expression;
        int totalArgSize = 0;

        // Phase 1: Resolve the function to be called.
        var func = FunctionResolver.ResolveFunctionCall(callExpr.Callee, context);

        // Phase 2: Handle struct return values (if any).
        var returnType = SemanticAnalyzer.AnalyzeFunctionReturnType(func, context);
        bool returnsStructByValue = TypeRepository.IsStruct(returnType) && !returnType.EndsWith("*");
        if (returnsStructByValue)
        {
            var size = MemoryLayoutManager.GetSizeOfType(returnType, context.CompilationUnit);
            Builder.AppendInstruction($"sub esp, {size}", "Make space for return value");
            Builder.AppendInstruction("push esp", "Push hidden return value pointer");
            totalArgSize += 4;
        }

        // Phase 3: Push all regular arguments.
        foreach (var arg in callExpr.Arguments.AsEnumerable().Reverse())
        {
            totalArgSize += Dispatcher.PushArgument(arg, context);
        }

        // Phase 4: Push `this` pointer (if it's a method) and dispatch the call.
        bool isMethodCall = func.OwnerStructName != null;
        if (isMethodCall)
        {
            if (callExpr.Callee is MemberAccessExpressionNode ma)
            {
                // Explicit call (obj.method() or ptr->method())
                if (ma.Operator.Type == TokenType.Arrow) Dispatcher.GenerateExpression(ma.Left, context);
                else LValueGenerator.GenerateLValueAddress(ma.Left, context);
            }
            else
            {
                // Implicit `this` call (method() from within another method)
                context.Symbols.TryGetSymbol("this", out int thisOffset, out _, out _);
                Builder.AppendInstruction($"mov eax, [ebp + {thisOffset}]", "Get implicit 'this' pointer");
            }
            Builder.AppendInstruction("push eax", "Push 'this' pointer");
            totalArgSize += 4;

            // Dispatch (virtual or static)
            if (func.IsVirtual || func.IsOverride)
            {
                var ownerTypeFqn = TypeRepository.GetFullyQualifiedOwnerName(func)!;
                var vtableIndex = VTableManager.GetMethodVTableIndex(ownerTypeFqn, func.Name);
                int thisPtrOnStackOffset = totalArgSize - 4;
                Builder.AppendInstruction($"mov eax, [esp + {thisPtrOnStackOffset}]", "Get 'this' from stack for vcall");
                Builder.AppendInstruction("mov eax, [eax]", "Get vtable pointer from object");
                Builder.AppendInstruction($"mov eax, [eax + {vtableIndex * 4}]", $"Get method address from vtable[{vtableIndex}]");
                Builder.AppendInstruction("call eax");
            }
            else
            {
                Builder.AppendInstruction($"call {NameMangler.Mangle(func)}");
            }
        }
        else
        {
            // Global function dispatch
            string calleeTarget = func.Body == null ? $"[{func.Name}]" : NameMangler.Mangle(func);
            if (func.Body == null) ExternalFunctions.Add(func.Name);
            Builder.AppendInstruction($"call {calleeTarget}");
        }

        // Phase 5: Cleanup stack.
        if (totalArgSize > 0)
        {
            Builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up args");
        }

        if (returnsStructByValue)
        {
            Builder.AppendInstruction("lea eax, [esp]", "Get address of hidden return temporary");
        }
    }
}