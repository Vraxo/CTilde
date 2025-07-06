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

        // Walk up the inheritance chain to find the member or property
        string? currentStructFqn = baseStructType;
        MemberVariableNode? member = null;
        PropertyDefinitionNode? property = null;
        StructDefinitionNode? definingStruct = null;

        while (currentStructFqn != null)
        {
            var structDef = _typeRepository.FindStruct(currentStructFqn);
            if (structDef == null)
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Type '{baseStructType}' is not a struct and has no members.", AstHelper.GetFirstToken(ma.Left).Line, AstHelper.GetFirstToken(ma.Left).Column));
                return "unknown";
            }

            member = structDef.Members.FirstOrDefault(m => m.Name.Value == ma.Member.Value);
            if (member != null)
            {
                definingStruct = structDef;
                break;
            }

            property = structDef.Properties.FirstOrDefault(p => p.Name.Value == ma.Member.Value);
            if (property != null)
            {
                definingStruct = structDef;
                break;
            }

            if (string.IsNullOrEmpty(structDef.BaseStructName)) break;

            var unit = _typeRepository.GetCompilationUnitForStruct(currentStructFqn);
            var baseTypeNode = new SimpleTypeNode(new Token(TokenType.Identifier, structDef.BaseStructName, -1, -1));
            currentStructFqn = _typeResolver.ResolveType(baseTypeNode, structDef.Namespace, unit);
        }

        if (member == null && property == null)
        {
            diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Type '{baseStructType}' has no member or property named '{ma.Member.Value}'.", ma.Member.Line, ma.Member.Column));
            return "unknown";
        }

        if (member != null)
        {
            if (member.AccessLevel == AccessSpecifier.Private)
            {
                if (context.CurrentFunction.OwnerStructName != definingStruct!.Name || context.CurrentFunction.Namespace != definingStruct.Namespace)
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
        else // property must be non-null
        {
            if (property!.AccessLevel == AccessSpecifier.Private)
            {
                if (context.CurrentFunction.OwnerStructName != definingStruct!.Name || context.CurrentFunction.Namespace != definingStruct.Namespace)
                {
                    diagnostics.Add(new Diagnostic(
                        context.CompilationUnit.FilePath,
                        $"Property '{definingStruct.Name}::{property.Name.Value}' is private and cannot be accessed from this context.",
                        ma.Member.Line,
                        ma.Member.Column
                    ));
                }
            }

            bool isLValue = ma.Parent is AssignmentExpressionNode assn && assn.Left == ma;
            if (isLValue) // It's a 'set'
            {
                if (!property.Accessors.Any(a => a.AccessorKeyword.Value == "set"))
                {
                    diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Property '{property.Name.Value}' does not have a 'set' accessor.", ma.Member.Line, ma.Member.Column));
                }
            }
            else // It's a 'get'
            {
                if (!property.Accessors.Any(a => a.AccessorKeyword.Value == "get"))
                {
                    diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Property '{property.Name.Value}' does not have a 'get' accessor.", ma.Member.Line, ma.Member.Column));
                }
            }

            return _typeResolver.ResolveType(property.Type, definingStruct!.Namespace, context.CompilationUnit);
        }
    }
}