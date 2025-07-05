using System.Collections.Generic;

namespace CTilde.Generator.ExpressionHandlers;

public abstract class ExpressionHandlerBase : IExpressionHandler
{
    protected readonly CodeGenerator CodeGenerator;
    protected AssemblyBuilder Builder => CodeGenerator.Builder;
    protected ExpressionGenerator Dispatcher => CodeGenerator.ExpressionGenerator;
    protected LValueGenerator LValueGenerator => CodeGenerator.ExpressionGenerator.LValueGenerator;
    protected TypeRepository TypeRepository => CodeGenerator.TypeRepository;
    protected TypeResolver TypeResolver => CodeGenerator.TypeResolver;
    protected FunctionResolver FunctionResolver => CodeGenerator.FunctionResolver;
    protected VTableManager VTableManager => CodeGenerator.VTableManager;
    protected MemoryLayoutManager MemoryLayoutManager => CodeGenerator.MemoryLayoutManager;
    protected SemanticAnalyzer SemanticAnalyzer => CodeGenerator.SemanticAnalyzer;
    protected HashSet<string> ExternalFunctions => CodeGenerator.ExternalFunctions;


    protected ExpressionHandlerBase(CodeGenerator codeGenerator)
    {
        CodeGenerator = codeGenerator;
    }

    public abstract void Generate(ExpressionNode expression, AnalysisContext context);
}