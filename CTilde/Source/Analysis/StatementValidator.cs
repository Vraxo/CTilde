using System;
using System.Collections.Generic;
using CTilde.Diagnostics;

namespace CTilde;

public class StatementValidator
{
    private readonly TypeRepository _typeRepository;
    private readonly TypeResolver _typeResolver;
    private readonly MemoryLayoutManager _memoryLayoutManager;
    private readonly ExpressionTypeAnalyzer _expressionTypeAnalyzer;

    public StatementValidator(TypeRepository typeRepository, TypeResolver typeResolver, MemoryLayoutManager memoryLayoutManager, ExpressionTypeAnalyzer expressionTypeAnalyzer)
    {
        _typeRepository = typeRepository;
        _typeResolver = typeResolver;
        _memoryLayoutManager = memoryLayoutManager;
        _expressionTypeAnalyzer = expressionTypeAnalyzer;
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
                var valueType = _expressionTypeAnalyzer.AnalyzeExpressionType(valueExpr, context, diagnostics);

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
            var initializerType = _expressionTypeAnalyzer.AnalyzeExpressionType(decl.Initializer, context, diagnostics);

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
                _expressionTypeAnalyzer.AnalyzeExpressionType(arg, context, diagnostics);
            }
            // TODO: Add constructor resolution and signature matching check here
        }
    }

    public void AnalyzeReturnStatement(ReturnStatementNode ret, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var funcReturnType = _expressionTypeAnalyzer.AnalyzeFunctionReturnType(context.CurrentFunction, context);

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

        var exprType = _expressionTypeAnalyzer.AnalyzeExpressionType(ret.Expression, context, diagnostics);

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

    public void AnalyzeDeleteStatement(DeleteStatementNode deleteStmt, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var exprType = _expressionTypeAnalyzer.AnalyzeExpressionType(deleteStmt.Expression, context, diagnostics);
        if (exprType != "unknown" && !exprType.EndsWith("*"))
        {
            var token = AstHelper.GetFirstToken(deleteStmt.Expression);
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"'delete' operator can only be applied to pointers, not type '{exprType}'.", token.Line, token.Column));
        }
    }
}