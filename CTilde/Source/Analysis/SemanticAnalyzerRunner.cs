using System.Collections.Generic;
using CTilde.Diagnostics;

namespace CTilde;

public class SemanticAnalyzerRunner
{
    private readonly ProgramNode _program;
    private readonly SemanticAnalyzer _analyzer;
    private readonly TypeResolver _typeResolver;
    private readonly FunctionResolver _functionResolver;
    private readonly MemoryLayoutManager _memoryLayoutManager;
    public List<Diagnostic> Diagnostics { get; } = new();

    public SemanticAnalyzerRunner(ProgramNode program, TypeRepository typeRepository, TypeResolver typeResolver, FunctionResolver functionResolver, MemoryLayoutManager memoryLayoutManager, SemanticAnalyzer analyzer)
    {
        _program = program;
        _analyzer = analyzer;
        _typeResolver = typeResolver;
        _functionResolver = functionResolver;
        _memoryLayoutManager = memoryLayoutManager;
    }

    public void Analyze()
    {
        foreach (var unit in _program.CompilationUnits)
        {
            foreach (var function in unit.Functions)
            {
                if (function.Body == null) continue;
                // Create a correct symbol table for each function
                var symbols = new SymbolTable(function, _typeResolver, _functionResolver, _memoryLayoutManager, unit);
                var context = new AnalysisContext(symbols, unit, function);
                WalkStatement(function.Body, context);
            }

            foreach (var s in unit.Structs)
            {
                foreach (var method in s.Methods)
                {
                    if (method.Body == null) continue;
                    var symbols = new SymbolTable(method, _typeResolver, _functionResolver, _memoryLayoutManager, unit);
                    var context = new AnalysisContext(symbols, unit, method);
                    WalkStatement(method.Body, context);
                }
                foreach (var ctor in s.Constructors)
                {
                    // For constructors, the "current function" is effectively the constructor itself for analysis context.
                    // We create a FunctionDeclarationNode that mirrors the constructor's context.
                    var dummyFunctionForContext = new FunctionDeclarationNode(
                        new Token(TokenType.Keyword, "void", -1, -1), 0, ctor.OwnerStructName,
                        ctor.Parameters, ctor.Body, ctor.OwnerStructName, ctor.AccessLevel,
                        false, false, ctor.Namespace
                    );
                    var symbols = new SymbolTable(ctor, _typeResolver, _functionResolver, _memoryLayoutManager, unit);
                    var context = new AnalysisContext(symbols, unit, dummyFunctionForContext);
                    WalkStatement(ctor.Body, context);
                }
                foreach (var dtor in s.Destructors)
                {
                    // For destructors, similarly, a dummy function node provides context.
                    var dummyFunctionForContext = new FunctionDeclarationNode(
                        new Token(TokenType.Keyword, "void", -1, -1), 0, dtor.OwnerStructName,
                        new List<ParameterNode>(), dtor.Body, dtor.OwnerStructName, dtor.AccessLevel,
                        dtor.IsVirtual, false, dtor.Namespace
                    );
                    var symbols = new SymbolTable(dtor, _typeResolver, _functionResolver, _memoryLayoutManager, unit);
                    var context = new AnalysisContext(symbols, unit, dummyFunctionForContext);
                    WalkStatement(dtor.Body, context);
                }
            }
        }
    }

    private void WalkStatement(StatementNode statement, AnalysisContext context)
    {
        switch (statement)
        {
            case BlockStatementNode block:
                foreach (var s in block.Statements) WalkStatement(s, context);
                break;
            case ExpressionStatementNode exprStmt:
                WalkExpression(exprStmt.Expression, context);
                break;
            case ReturnStatementNode retStmt:
                if (retStmt.Expression != null) WalkExpression(retStmt.Expression, context);
                break;
            case IfStatementNode ifStmt:
                WalkExpression(ifStmt.Condition, context);
                WalkStatement(ifStmt.ThenBody, context);
                if (ifStmt.ElseBody != null) WalkStatement(ifStmt.ElseBody, context);
                break;
            case WhileStatementNode whileStmt:
                WalkExpression(whileStmt.Condition, context);
                WalkStatement(whileStmt.Body, context);
                break;
            case DeclarationStatementNode decl:
                _analyzer.AnalyzeDeclarationStatement(decl, context, Diagnostics);
                break;
            case DeleteStatementNode deleteStmt:
                WalkExpression(deleteStmt.Expression, context);
                break;
        }
    }

    private void WalkExpression(ExpressionNode expression, AnalysisContext context)
    {
        _analyzer.AnalyzeExpressionType(expression, context, Diagnostics);
    }
}