﻿using System;
using System.Collections.Generic;
using System.Linq;
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
        try
        {
            // Iterate over a copy of the compilation units, as monomorphization might add new structs
            // to them, which in turn adds new methods that need analysis.
            // A more sophisticated approach might use a worklist, but this is simpler for now.
            bool changed;
            do
            {
                changed = false;
                var currentStructs = _program.CompilationUnits.SelectMany(cu => cu.Structs).ToList();

                foreach (var unit in _program.CompilationUnits.ToList())
                {
                    foreach (var function in unit.Functions.ToList())
                    {
                        if (function.Body is null) continue;
                        var symbols = new SymbolTable(function, _typeResolver, _functionResolver, _memoryLayoutManager, unit);
                        var context = new AnalysisContext(symbols, unit, function);
                        WalkStatement(function.Body, context);
                        CheckForUnusedVariables(function, context);
                    }

                    foreach (var s in unit.Structs.ToList())
                    {
                        // If it's a generic template (e.g. struct List<T>), skip analysis.
                        // It will be monomorphized and analyzed on-demand when instantiated.
                        if (s.GenericParameters.Any())
                        {
                            continue;
                        }

                        foreach (var method in s.Methods.ToList())
                        {
                            if (method.Body is null) continue;
                            var symbols = new SymbolTable(method, _typeResolver, _functionResolver, _memoryLayoutManager, unit);
                            var context = new AnalysisContext(symbols, unit, method);
                            WalkStatement(method.Body, context);
                            CheckForUnusedVariables(method, context);
                        }
                        foreach (var ctor in s.Constructors.ToList())
                        {
                            var dummyFunctionForContext = new FunctionDeclarationNode(
                                new SimpleTypeNode(new Token(TokenType.Keyword, "void", -1, -1)), ctor.OwnerStructName,
                                ctor.Parameters, ctor.Body, ctor.OwnerStructName, ctor.AccessLevel,
                                false, false, ctor.Namespace
                            );
                            var symbols = new SymbolTable(ctor, _typeResolver, _functionResolver, _memoryLayoutManager, unit);
                            var context = new AnalysisContext(symbols, unit, dummyFunctionForContext);
                            WalkStatement(ctor.Body, context);
                            CheckForUnusedVariables(dummyFunctionForContext, context);
                        }
                        foreach (var dtor in s.Destructors.ToList())
                        {
                            var dummyFunctionForContext = new FunctionDeclarationNode(
                                new SimpleTypeNode(new Token(TokenType.Keyword, "void", -1, -1)), dtor.OwnerStructName,
                                new List<ParameterNode>(), dtor.Body, dtor.OwnerStructName, dtor.AccessLevel,
                                dtor.IsVirtual, false, dtor.Namespace
                            );
                            var symbols = new SymbolTable(dtor, _typeResolver, _functionResolver, _memoryLayoutManager, unit);
                            var context = new AnalysisContext(symbols, unit, dummyFunctionForContext);
                            WalkStatement(dtor.Body, context);
                            CheckForUnusedVariables(dummyFunctionForContext, context);
                        }
                    }
                }

                var newStructs = _program.CompilationUnits.SelectMany(cu => cu.Structs).ToList();
                if (newStructs.Count > currentStructs.Count)
                {
                    changed = true;
                }

            } while (changed);
        }
        catch (Exception ex)
        {
            // Catch any unexpected crashes during analysis and report them as a diagnostic.
            // This ensures that parser-level diagnostics are still shown.
            Diagnostics.Add(new Diagnostic("Compiler", $"FATAL ANALYSIS ERROR: {ex.Message}", 0, 0));
        }
    }

    private void CheckForUnusedVariables(FunctionDeclarationNode function, AnalysisContext context)
    {
        var localDeclarations = new List<DeclarationStatementNode>();
        if (function.Body is not null)
        {
            FindAllDeclarations(function.Body, localDeclarations);
        }

        var unreadLocals = context.Symbols.GetUnreadLocals().Select(ul => ul.Name).ToHashSet();
        foreach (var decl in localDeclarations)
        {
            if (unreadLocals.Contains(decl.Identifier.Value))
            {
                Diagnostics.Add(new Diagnostic(
                    context.CompilationUnit.FilePath,
                    $"Unused variable '{decl.Identifier.Value}'.",
                    decl.Identifier.Line,
                    decl.Identifier.Column,
                    DiagnosticSeverity.Warning
                ));
            }
        }
    }

    private void FindAllDeclarations(StatementNode stmt, List<DeclarationStatementNode> declarations)
    {
        switch (stmt)
        {
            case DeclarationStatementNode d:
                declarations.Add(d);
                break;
            case BlockStatementNode b:
                foreach (var s in b.Statements) FindAllDeclarations(s, declarations);
                break;
            case IfStatementNode i:
                FindAllDeclarations(i.ThenBody, declarations);
                if (i.ElseBody is not null) FindAllDeclarations(i.ElseBody, declarations);
                break;
            case WhileStatementNode w:
                FindAllDeclarations(w.Body, declarations);
                break;
        }
    }


    private void WalkStatement(StatementNode statement, AnalysisContext context, bool isReachable = true)
    {
        // Check for unreachable code first
        if (!isReachable)
        {
            var token = AstHelper.GetFirstToken(statement);
            // Don't flag closing braces as unreachable
            if (token.Type != TokenType.RightBrace)
            {
                Diagnostics.Add(new Diagnostic(
                    context.CompilationUnit.FilePath,
                    "Unreachable code detected.",
                    token.Line,
                    token.Column,
                    DiagnosticSeverity.Warning
                ));
            }
            return; // Do not process this statement further
        }

        _analyzer.AnalyzeStatement(statement, context, Diagnostics);

        switch (statement)
        {
            case BlockStatementNode block:
                bool blockIsReachable = true;
                foreach (var s in block.Statements)
                {
                    WalkStatement(s, context, blockIsReachable);
                    if (s is ReturnStatementNode) blockIsReachable = false;
                }
                break;
            case IfStatementNode ifStmt:
                WalkStatement(ifStmt.ThenBody, context);
                if (ifStmt.ElseBody is not null) WalkStatement(ifStmt.ElseBody, context);
                break;
            case WhileStatementNode whileStmt:
                WalkStatement(whileStmt.Body, context);
                break;
            // Leaf statements that have already been analyzed and have no children to traverse.
            case ExpressionStatementNode:
            case ReturnStatementNode:
            case DeclarationStatementNode:
            case DeleteStatementNode:
                break;
        }
    }
}