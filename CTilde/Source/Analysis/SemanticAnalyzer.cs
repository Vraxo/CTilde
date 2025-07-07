using CTilde.Analysis.ExpressionAnalyzers;
using CTilde.Diagnostics;

namespace CTilde;

public class SemanticAnalyzer
{
    private readonly TypeRepository _typeRepository;
    private readonly TypeResolver _typeResolver;
    private readonly FunctionResolver _functionResolver;
    private readonly MemoryLayoutManager _memoryLayoutManager;

    private readonly Dictionary<Type, IExpressionAnalyzer> _expressionAnalyzers;

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

    public void AnalyzeDeclarationStatement(DeclarationStatementNode decl, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        string declaredTypeFqn;

        try
        {
            declaredTypeFqn = _typeResolver.ResolveType(decl.Type, context.CurrentFunction.Namespace, context.CompilationUnit);
        }
        catch (InvalidOperationException ex)
        {
            Token token = decl.Type.GetFirstToken();

            diagnostics.Add(new(
                context.CompilationUnit.FilePath,
                ex.Message,
                token.Line,
                token.Column));

            declaredTypeFqn = "unknown";
        }

        if (declaredTypeFqn == "unknown")
        {
            return;
        }

        if (decl.Initializer is InitializerListExpressionNode il)
        {
            StructDefinitionNode? structDef = _typeRepository.FindStruct(declaredTypeFqn);

            if (structDef is null)
            {
                Token token = decl.Type.GetFirstToken();

                diagnostics.Add(new(
                    context.CompilationUnit.FilePath,
                    $"Type '{declaredTypeFqn}' is not a struct and cannot be initialized with an initializer list.",
                    token.Line,
                    token.Column));

                return;
            }

            List<(string name, string type, int offset, bool isConst)> members = _memoryLayoutManager.GetAllMembers(declaredTypeFqn, context.CompilationUnit);
            
            if (il.Values.Count > members.Count)
            {
                diagnostics.Add(new(
                    context.CompilationUnit.FilePath,
                    $"Too many elements in initializer list for type '{structDef.Name}'.",
                    il.OpeningBrace.Line,
                    il.OpeningBrace.Column));
            }

            for (int i = 0; i < Math.Min(il.Values.Count, members.Count); i++)
            {
                (string name, string type, _, _) = members[i];
                ExpressionNode valueExpr = il.Values[i];
                string valueType = AnalyzeExpressionType(valueExpr, context, diagnostics);

                bool isIntToCharLiteralConversion =
                    valueType == "int" && type == "char" && valueExpr is IntegerLiteralNode;

                if (valueType != "unknown" && type != valueType && !isIntToCharLiteralConversion)
                {
                    Token token = AstHelper.GetFirstToken(valueExpr);

                    diagnostics.Add(new(
                        context.CompilationUnit.FilePath,
                        $"Cannot initialize member '{name}' (type '{type}') with a value of type '{valueType}'.",
                        token.Line,
                        token.Column));
                }
            }
        }
        else if (decl.Initializer is not null)
        {
            string initializerType = AnalyzeExpressionType(decl.Initializer, context, diagnostics);
            bool isIntToCharLiteralConversion = declaredTypeFqn == "char" && initializerType == "int" && decl.Initializer is IntegerLiteralNode;
            bool isIntToPointerConversion = declaredTypeFqn.EndsWith('*') && initializerType == "int";

            if (initializerType == "unknown" || declaredTypeFqn == initializerType || isIntToCharLiteralConversion || isIntToPointerConversion)
            {
                return;
            }

            diagnostics.Add(new(
                context.CompilationUnit.FilePath,
                $"Cannot implicitly convert type '{initializerType}' to '{declaredTypeFqn}'.",
                AstHelper.GetFirstToken(decl.Initializer).Line,
                AstHelper.GetFirstToken(decl.Initializer).Column));
        }
        else if (decl.ConstructorArguments is not null)
        {
            foreach (ExpressionNode arg in decl.ConstructorArguments)
            {
                AnalyzeExpressionType(arg, context, diagnostics);
            }
        }
    }

    public string GetFunctionReturnType(FunctionDeclarationNode func, AnalysisContext context)
    {
        return _typeResolver.ResolveType(func.ReturnType, func.Namespace, context.CompilationUnit);
    }

    public void AnalyzeReturnStatement(ReturnStatementNode ret, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        string funcReturnType = GetFunctionReturnType(context.CurrentFunction, context);

        if (ret.Expression is null)
        {
            if (funcReturnType != "void")
            {
                Token _token = AstHelper.GetFirstToken(ret);

                diagnostics.Add(new(
                    context.CompilationUnit.FilePath,
                    $"Non-void function '{context.CurrentFunction.Name}' must return a value.",
                    _token.Line,
                    _token.Column));
            }

            return;
        }

        if (funcReturnType == "void")
        {
            Token _token = AstHelper.GetFirstToken(ret);

            diagnostics.Add(new(
                context.CompilationUnit.FilePath,
                $"Cannot return a value from void function '{context.CurrentFunction.Name}'.",
                _token.Line,
                _token.Column));

            return;
        }

        string exprType = AnalyzeExpressionType(ret.Expression, context, diagnostics);

        if (exprType == "unknown" || exprType == funcReturnType)
        {
            return;
        }

        bool isIntToCharLiteralConversion = funcReturnType == "char" && exprType == "int" && ret.Expression is IntegerLiteralNode;
        bool isGenericReturn = funcReturnType.Length == 1 && char.IsUpper(funcReturnType[0]) || exprType.Length == 1 && char.IsUpper(exprType[0]);

        if (isIntToCharLiteralConversion || isGenericReturn)
        {
            return;
        }

        Token token = AstHelper.GetFirstToken(ret.Expression);

        diagnostics.Add(new(
            context.CompilationUnit.FilePath,
            $"Cannot implicitly convert return type '{exprType}' to '{funcReturnType}'.",
            token.Line,
            token.Column));
    }

    public void AnalyzeDeleteStatement(DeleteStatementNode deleteStmt, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        string exprType = AnalyzeExpressionType(deleteStmt.Expression, context, diagnostics);

        if (exprType == "unknown" || exprType.EndsWith('*'))
        {
            return;
        }

        Token token = AstHelper.GetFirstToken(deleteStmt.Expression);

        diagnostics.Add(new(
            context.CompilationUnit.FilePath,
            $"'delete' operator can only be applied to pointers, not type '{exprType}'.",
            token.Line,
            token.Column));
    }
}