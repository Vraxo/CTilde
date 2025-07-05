using System;
using System.Collections.Generic;
using System.Linq;
using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public class MemberAccessExpressionAnalyzer : ExpressionAnalyzerBase
{
    public MemberAccessExpressionAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var ma = (MemberAccessExpressionNode)expr;

        var leftType = _semanticAnalyzer.AnalyzeExpressionType(ma.Left, context, diagnostics);
        if (leftType == "unknown") return "unknown";

        string baseStructType = leftType.TrimEnd('*');

        // Walk up the inheritance chain to find the member
        string? currentStructFqn = baseStructType;
        MemberVariableNode? member = null;
        StructDefinitionNode? definingStruct = null;

        while (currentStructFqn != null)
        {
            var structDef = _typeRepository.FindStruct(currentStructFqn);
            if (structDef == null)
            {
                // This case should be handled by the initial struct check
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Type '{baseStructType}' is not a struct and has no members.", AstHelper.GetFirstToken(ma.Left).Line, AstHelper.GetFirstToken(ma.Left).Column));
                return "unknown";
            }

            member = structDef.Members.FirstOrDefault(m => m.Name.Value == ma.Member.Value);
            if (member != null)
            {
                definingStruct = structDef;
                break;
            }

            if (string.IsNullOrEmpty(structDef.BaseStructName)) break;

            var unit = _typeRepository.GetCompilationUnitForStruct(currentStructFqn);
            var baseTypeNode = new SimpleTypeNode(new Token(TokenType.Identifier, structDef.BaseStructName, -1, -1));
            currentStructFqn = _typeResolver.ResolveType(baseTypeNode, structDef.Namespace, unit);
        }

        if (member == null || definingStruct == null)
        {
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Struct '{baseStructType}' has no member named '{ma.Member.Value}'.", ma.Member.Line, ma.Member.Column));
            return "unknown";
        }

        // ** ENFORCE ACCESS SPECIFIER **
        if (member.AccessLevel == AccessSpecifier.Private)
        {
            // Access is allowed only if the current function is a method of the struct that DEFINED the member.
            if (context.CurrentFunction.OwnerStructName != definingStruct.Name || context.CurrentFunction.Namespace != definingStruct.Namespace)
            {
                diagnostics.Add(new Diagnostic(
                    context.CompilationUnit.FilePath,
                    $"Member '{definingStruct.Name}::{member.Name.Value}' is private and cannot be accessed from this context.",
                    ma.Member.Line,
                    ma.Member.Column
                ));
            }
        }

        var (_, resolvedMemberType) = _memoryLayoutManager.GetMemberInfo(baseStructType, ma.Member.Value, context.CompilationUnit);
        return resolvedMemberType;
    }
}