using CTilde.Diagnostics;

namespace CTilde.Analysis.StatementAnalyzers;

public class ReturnStatementAnalyzer : StatementAnalyzerBase
{
    public ReturnStatementAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override void Analyze(StatementNode stmt, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var ret = (ReturnStatementNode)stmt;
        string funcReturnType = _semanticAnalyzer.GetFunctionReturnType(context.CurrentFunction, context);

        if (ret.Expression is null)
        {
            if (funcReturnType != "void")
            {
                Token _token = AstHelper.GetFirstToken(ret);
                diagnostics.Add(new(context.CompilationUnit.FilePath, $"Non-void function '{context.CurrentFunction.Name}' must return a value.", _token.Line, _token.Column));
            }
            return;
        }

        if (funcReturnType == "void")
        {
            Token _token = AstHelper.GetFirstToken(ret);
            diagnostics.Add(new(context.CompilationUnit.FilePath, $"Cannot return a value from void function '{context.CurrentFunction.Name}'.", _token.Line, _token.Column));
            return;
        }

        string exprType = _semanticAnalyzer.AnalyzeExpressionType(ret.Expression, context, diagnostics);
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
        diagnostics.Add(new(context.CompilationUnit.FilePath, $"Cannot implicitly convert return type '{exprType}' to '{funcReturnType}'.", token.Line, token.Column));
    }
}