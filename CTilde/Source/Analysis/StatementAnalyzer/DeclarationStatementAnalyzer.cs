using CTilde.Diagnostics;

namespace CTilde.Analysis.StatementAnalyzers;

public class DeclarationStatementAnalyzer : StatementAnalyzerBase
{
    public DeclarationStatementAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override void Analyze(StatementNode stmt, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var decl = (DeclarationStatementNode)stmt;
        string declaredTypeFqn;

        try
        {
            declaredTypeFqn = _typeResolver.ResolveType(decl.Type, context.CurrentFunction.Namespace, context.CompilationUnit);
        }
        catch (InvalidOperationException ex)
        {
            Token token = decl.Type.GetFirstToken();
            diagnostics.Add(new(context.CompilationUnit.FilePath, ex.Message, token.Line, token.Column));
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
                diagnostics.Add(new(context.CompilationUnit.FilePath, $"Type '{declaredTypeFqn}' is not a struct and cannot be initialized with an initializer list.", token.Line, token.Column));
                return;
            }

            List<(string name, string type, int offset, bool isConst)> members = _memoryLayoutManager.GetAllMembers(declaredTypeFqn, context.CompilationUnit);
            if (il.Values.Count > members.Count)
            {
                diagnostics.Add(new(context.CompilationUnit.FilePath, $"Too many elements in initializer list for type '{structDef.Name}'.", il.OpeningBrace.Line, il.OpeningBrace.Column));
            }

            for (int i = 0; i < Math.Min(il.Values.Count, members.Count); i++)
            {
                (string name, string type, _, _) = members[i];
                ExpressionNode valueExpr = il.Values[i];
                string valueType = _semanticAnalyzer.AnalyzeExpressionType(valueExpr, context, diagnostics);

                bool isIntToCharLiteralConversion = valueType == "int" && type == "char" && valueExpr is IntegerLiteralNode;
                if (valueType != "unknown" && type != valueType && !isIntToCharLiteralConversion)
                {
                    Token token = AstHelper.GetFirstToken(valueExpr);
                    diagnostics.Add(new(context.CompilationUnit.FilePath, $"Cannot initialize member '{name}' (type '{type}') with a value of type '{valueType}'.", token.Line, token.Column));
                }
            }
        }
        else if (decl.Initializer is not null)
        {
            string initializerType = _semanticAnalyzer.AnalyzeExpressionType(decl.Initializer, context, diagnostics);
            bool isIntToCharLiteralConversion = declaredTypeFqn == "char" && initializerType == "int" && decl.Initializer is IntegerLiteralNode;
            bool isIntToPointerConversion = declaredTypeFqn.EndsWith('*') && initializerType == "int";

            if (initializerType == "unknown" || declaredTypeFqn == initializerType || isIntToCharLiteralConversion || isIntToPointerConversion)
            {
                return;
            }

            diagnostics.Add(new(context.CompilationUnit.FilePath, $"Cannot implicitly convert type '{initializerType}' to '{declaredTypeFqn}'.", AstHelper.GetFirstToken(decl.Initializer).Line, AstHelper.GetFirstToken(decl.Initializer).Column));
        }
        else if (decl.ConstructorArguments is not null)
        {
            foreach (ExpressionNode arg in decl.ConstructorArguments)
            {
                _semanticAnalyzer.AnalyzeExpressionType(arg, context, diagnostics);
            }
        }
    }
}