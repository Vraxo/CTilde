using CTilde.Analysis.ExpressionAnalyzers;
using CTilde.Analysis.StatementAnalyzers;
using CTilde.Diagnostics;

namespace CTilde;

public class SemanticAnalyzer
{
    private readonly TypeRepository _typeRepository;
    private readonly TypeResolver _typeResolver;
    private readonly FunctionResolver _functionResolver;
    private readonly MemoryLayoutManager _memoryLayoutManager;

    private readonly Dictionary<Type, IExpressionAnalyzer> _expressionAnalyzers;
    private readonly Dictionary<Type, IStatementAnalyzer> _statementAnalyzers;

    public SemanticAnalyzer(TypeRepository typeRepository, TypeResolver typeResolver, FunctionResolver functionResolver, MemoryLayoutManager memoryLayoutManager)
    {
        _typeRepository = typeRepository;
        _typeResolver = typeResolver;
        _functionResolver = functionResolver;
        _memoryLayoutManager = memoryLayoutManager;

        _expressionAnalyzers = new()
        {
            { typeof(IntegerLiteralNode), new IntegerLiteralAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(StringLiteralNode), new StringLiteralAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(SizeofExpressionNode), new SizeofExpressionAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(VariableExpressionNode), new VariableExpressionAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(AssignmentExpressionNode), new AssignmentExpressionAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(MemberAccessExpressionNode), new MemberAccessExpressionAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(UnaryExpressionNode), new UnaryExpressionAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(CallExpressionNode), new CallExpressionAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(QualifiedAccessExpressionNode), new QualifiedAccessExpressionAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(NewExpressionNode), new NewExpressionAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(BinaryExpressionNode), new BinaryExpressionAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(InitializerListExpressionNode), new InitializerListExpressionAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) }
        };

        _statementAnalyzers = new()
        {
            { typeof(DeclarationStatementNode), new DeclarationStatementAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(ReturnStatementNode), new ReturnStatementAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(DeleteStatementNode), new DeleteStatementAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(ExpressionStatementNode), new ExpressionStatementAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(IfStatementNode), new IfStatementAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) },
            { typeof(WhileStatementNode), new WhileStatementAnalyzer(this, typeRepository, typeResolver, functionResolver, memoryLayoutManager) }
        };
    }

    public string AnalyzeExpressionType(ExpressionNode expr, AnalysisContext context)
    {
        List<Diagnostic> diagnostics = [];
        string type = AnalyzeExpressionType(expr, context, diagnostics);

        if (diagnostics.Count != 0)
        {
            throw new InvalidOperationException($"Internal Compiler Error: Unexpected semantic error during code generation: {diagnostics.First().Message}");
        }

        return type;
    }

    public string AnalyzeExpressionType(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        try
        {
            if (_expressionAnalyzers.TryGetValue(expr.GetType(), out var analyzer))
            {
                return analyzer.Analyze(expr, context, diagnostics);
            }

            throw new NotImplementedException($"AnalyzeExpressionType not implemented for {expr.GetType().Name}");
        }
        catch (InvalidOperationException ex)
        {
            Token token = AstHelper.GetFirstToken(expr);

            diagnostics.Add(new(
                context.CompilationUnit.FilePath,
                ex.Message,
                token.Line,
                token.Column));

            return "unknown";
        }
    }

    public void AnalyzeStatement(StatementNode stmt, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        try
        {
            if (_statementAnalyzers.TryGetValue(stmt.GetType(), out var analyzer))
            {
                analyzer.Analyze(stmt, context, diagnostics);
            }
        }
        catch (InvalidOperationException ex)
        {
            Token token = AstHelper.GetFirstToken(stmt);

            diagnostics.Add(new(
                context.CompilationUnit.FilePath,
                ex.Message,
                token.Line,
                token.Column));
        }
    }

    public string GetFunctionReturnType(FunctionDeclarationNode func, AnalysisContext context)
    {
        return _typeResolver.ResolveType(func.ReturnType, func.Namespace, context.CompilationUnit);
    }
}