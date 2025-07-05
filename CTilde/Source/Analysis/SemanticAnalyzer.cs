using System;
using System.Collections.Generic;
using System.Linq;
using CTilde.Diagnostics;
using CTilde.Analysis.ExpressionAnalyzers; // New using directive

namespace CTilde;

public class SemanticAnalyzer
{
    private readonly TypeRepository _typeRepository;
    private readonly TypeResolver _typeResolver;
    private readonly FunctionResolver _functionResolver;
    private readonly MemoryLayoutManager _memoryLayoutManager;

    // New: Dictionary for dispatching to expression analyzers
    private readonly Dictionary<Type, IExpressionAnalyzer> _expressionAnalyzers;

    public SemanticAnalyzer(TypeRepository typeRepository, TypeResolver typeResolver, FunctionResolver functionResolver, MemoryLayoutManager memoryLayoutManager)
    {
        _typeRepository = typeRepository;
        _typeResolver = typeResolver;
        _functionResolver = functionResolver;
        _memoryLayoutManager = memoryLayoutManager;

        // Initialize expression analyzers
        _expressionAnalyzers = new Dictionary<Type, IExpressionAnalyzer>
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

    // New overload for use during code generation.
    // No new diagnostics are expected at this stage. If any are found, it indicates
    // a compiler bug, so we throw an exception.
    public string AnalyzeExpressionType(ExpressionNode expr, AnalysisContext context)
    {
        var diagnostics = new List<Diagnostic>();
        var type = AnalyzeExpressionType(expr, context, diagnostics);
        if (diagnostics.Any())
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
            // The logic below still relies on some exceptions for flow control.
            // We convert them to diagnostics here.
            var token = AstHelper.GetFirstToken(expr);
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, ex.Message, token.Line, token.Column));
            return "unknown"; // Return a sentinel type on error
        }
    }

    // This method handles statement-level analysis. It's not an expression analyzer itself.
    public void AnalyzeDeclarationStatement(DeclarationStatementNode decl, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        string declaredTypeFqn;
        try
        {
            declaredTypeFqn = _typeResolver.ResolveType(decl.Type, context.CurrentFunction.Namespace, context.CompilationUnit);
        }
        catch (InvalidOperationException ex)
        {
            var token = decl.Type.GetFirstToken();
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, ex.Message, token.Line, token.Column));
            declaredTypeFqn = "unknown"; // Sentinel type
        }

        if (declaredTypeFqn == "unknown") return; // Stop analysis if type resolution failed

        if (decl.Initializer is InitializerListExpressionNode il)
        {
            var structDef = _typeRepository.FindStruct(declaredTypeFqn);

            if (structDef == null)
            {
                var token = decl.Type.GetFirstToken();
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Type '{declaredTypeFqn}' is not a struct and cannot be initialized with an initializer list.", token.Line, token.Column));
                return;
            }

            var members = _memoryLayoutManager.GetAllMembers(declaredTypeFqn, context.CompilationUnit);
            if (il.Values.Count > members.Count)
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Too many elements in initializer list for type '{structDef.Name}'.", il.OpeningBrace.Line, il.OpeningBrace.Column));
                // Continue to check existing members for other errors
            }

            for (int i = 0; i < Math.Min(il.Values.Count, members.Count); i++) // Only iterate up to min to avoid IndexOutOfRangeException on too many values
            {
                var member = members[i];
                var valueExpr = il.Values[i];
                var valueType = AnalyzeExpressionType(valueExpr, context, diagnostics);

                // Allow int literal to char conversion
                bool isIntToCharLiteralConversion =
                    valueType == "int" && member.type == "char" && valueExpr is IntegerLiteralNode;

                if (valueType != "unknown" && member.type != valueType && !isIntToCharLiteralConversion)
                {
                    var token = AstHelper.GetFirstToken(valueExpr);
                    diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Cannot initialize member '{member.name}' (type '{member.type}') with a value of type '{valueType}'.", token.Line, token.Column));
                }
            }
        }
        else if (decl.Initializer != null)
        {
            // Analyze other initializers (e.g., assignment)
            var initializerType = AnalyzeExpressionType(decl.Initializer, context, diagnostics);

            // Allow int literal to char conversion
            bool isIntToCharLiteralConversion = declaredTypeFqn == "char" && initializerType == "int" && decl.Initializer is IntegerLiteralNode;

            // Allow int to pointer conversion (for malloc etc)
            bool isIntToPointerConversion = declaredTypeFqn.EndsWith("*") && initializerType == "int";

            if (initializerType != "unknown" && declaredTypeFqn != initializerType && !isIntToCharLiteralConversion && !isIntToPointerConversion)
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Cannot implicitly convert type '{initializerType}' to '{declaredTypeFqn}'.", AstHelper.GetFirstToken(decl.Initializer).Line, AstHelper.GetFirstToken(decl.Initializer).Column));
            }
        }
        else if (decl.ConstructorArguments != null)
        {
            foreach (var arg in decl.ConstructorArguments)
            {
                AnalyzeExpressionType(arg, context, diagnostics);
            }
            // TODO: Add constructor resolution and signature matching check here
        }
    }


    // Renamed from AnalyzeFunctionReturnType to GetFunctionReturnType as it's a pure getter
    public string GetFunctionReturnType(FunctionDeclarationNode func, AnalysisContext context)
    {
        return _typeResolver.ResolveType(func.ReturnType, func.Namespace, context.CompilationUnit);
    }

    // This method handles statement-level analysis.
    public void AnalyzeReturnStatement(ReturnStatementNode ret, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var funcReturnType = GetFunctionReturnType(context.CurrentFunction, context);

        if (ret.Expression == null)
        {
            if (funcReturnType != "void")
            {
                var token = AstHelper.GetFirstToken(ret);
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Non-void function '{context.CurrentFunction.Name}' must return a value.", token.Line, token.Column));
            }
            return;
        }

        if (funcReturnType == "void")
        {
            var token = AstHelper.GetFirstToken(ret);
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Cannot return a value from void function '{context.CurrentFunction.Name}'.", token.Line, token.Column));
            return;
        }

        var exprType = AnalyzeExpressionType(ret.Expression, context, diagnostics);

        if (exprType != "unknown" && exprType != funcReturnType)
        {
            // Allow int to char conversion
            bool isIntToCharLiteralConversion = funcReturnType == "char" && exprType == "int" && ret.Expression is IntegerLiteralNode;
            // HACK: Allow returning a generic type T where a concrete type is expected (or vice versa)
            bool isGenericReturn = (funcReturnType.Length == 1 && char.IsUpper(funcReturnType[0])) || (exprType.Length == 1 && char.IsUpper(exprType[0]));

            if (!isIntToCharLiteralConversion && !isGenericReturn)
            {
                var token = AstHelper.GetFirstToken(ret.Expression);
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Cannot implicitly convert return type '{exprType}' to '{funcReturnType}'.", token.Line, token.Column));
            }
        }
    }

    // This method handles statement-level analysis.
    public void AnalyzeDeleteStatement(DeleteStatementNode deleteStmt, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var exprType = AnalyzeExpressionType(deleteStmt.Expression, context, diagnostics);
        if (exprType != "unknown" && !exprType.EndsWith("*"))
        {
            var token = AstHelper.GetFirstToken(deleteStmt.Expression);
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"'delete' operator can only be applied to pointers, not type '{exprType}'.", token.Line, token.Column));
        }
    }
}