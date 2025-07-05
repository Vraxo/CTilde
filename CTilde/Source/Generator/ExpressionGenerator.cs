using System;
using System.Collections.Generic;
using System.Linq;
using CTilde.Generator;
using CTilde.Generator.ExpressionHandlers;

namespace CTilde;

public class ExpressionGenerator
{
    private readonly CodeGenerator _codeGenerator;
    internal readonly LValueGenerator LValueGenerator;
    private readonly Dictionary<Type, IExpressionHandler> _handlers;

    private AssemblyBuilder Builder => _codeGenerator.Builder;
    private TypeRepository TypeRepository => _codeGenerator.TypeRepository;
    private SemanticAnalyzer SemanticAnalyzer => _codeGenerator.SemanticAnalyzer;
    private MemoryLayoutManager MemoryLayoutManager => _codeGenerator.MemoryLayoutManager;

    public ExpressionGenerator(CodeGenerator codeGenerator)
    {
        _codeGenerator = codeGenerator;
        LValueGenerator = new LValueGenerator(_codeGenerator);
        _handlers = new Dictionary<Type, IExpressionHandler>
        {
            { typeof(IntegerLiteralNode), new IntegerLiteralHandler(_codeGenerator) },
            { typeof(StringLiteralNode), new StringLiteralHandler(_codeGenerator) },
            { typeof(VariableExpressionNode), new VariableExpressionHandler(_codeGenerator) },
            { typeof(UnaryExpressionNode), new UnaryExpressionHandler(_codeGenerator) },
            { typeof(MemberAccessExpressionNode), new MemberAccessExpressionHandler(_codeGenerator) },
            { typeof(AssignmentExpressionNode), new AssignmentExpressionHandler(_codeGenerator) },
            { typeof(BinaryExpressionNode), new BinaryExpressionHandler(_codeGenerator) },
            { typeof(CallExpressionNode), new CallExpressionHandler(_codeGenerator) },
            { typeof(QualifiedAccessExpressionNode), new QualifiedAccessExpressionHandler(_codeGenerator) },
            { typeof(NewExpressionNode), new NewExpressionHandler(_codeGenerator) },
            { typeof(SizeofExpressionNode), new SizeofExpressionHandler(_codeGenerator) }
        };
    }

    public void GenerateExpression(ExpressionNode expression, AnalysisContext context)
    {
        if (_handlers.TryGetValue(expression.GetType(), out var handler))
        {
            handler.Generate(expression, context);
        }
        else
        {
            throw new NotImplementedException($"Expr: {expression.GetType().Name}");
        }
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
        LValueGenerator.GenerateLValueAddress(expression, context);
    }
}