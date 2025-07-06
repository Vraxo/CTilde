using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public class MemberAccessExpressionAnalyzer : ExpressionAnalyzerBase
{
    private record MemberSearchResult(
        MemberVariableNode? Member,
        PropertyDefinitionNode? Property,
        StructDefinitionNode DefiningStruct);

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

        string leftType = _semanticAnalyzer.AnalyzeExpressionType(ma.Left, context, diagnostics);
        
        if (leftType == "unknown")
        {
            return "unknown";
        }

        string baseStructType = leftType.TrimEnd('*');

        MemberSearchResult? searchResult = FindMemberOrPropertyInHierarchy(baseStructType, ma, context, diagnostics);

        if (searchResult is null)
        {
            return "unknown";
        }

        if (searchResult.Member is not null)
        {
            return AnalyzeFoundMember(searchResult.Member, searchResult.DefiningStruct, baseStructType, ma, context, diagnostics);
        }

        return AnalyzeFoundProperty(searchResult.Property!, searchResult.DefiningStruct, ma, context, diagnostics);
    }

    private MemberSearchResult? FindMemberOrPropertyInHierarchy(string baseStructFqn, MemberAccessExpressionNode ma, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        string? currentStructFqn = baseStructFqn;

        while (currentStructFqn is not null)
        {
            StructDefinitionNode? structDef = _typeRepository.FindStruct(currentStructFqn);
            
            if (structDef is null)
            {
                diagnostics.Add(new(
                    context.CompilationUnit.FilePath,
                    $"Type '{baseStructFqn}' is not a defined struct.",
                    AstHelper.GetFirstToken(ma.Left).Line,
                    AstHelper.GetFirstToken(ma.Left).Column));

                return null;
            }

            MemberVariableNode? member = structDef.Members.FirstOrDefault(m => m.Name.Value == ma.Member.Value);
            
            if (member is not null)
            {
                return new MemberSearchResult(member, null, structDef);
            }

            PropertyDefinitionNode? property = structDef.Properties.FirstOrDefault(p => p.Name.Value == ma.Member.Value);
            
            if (property is not null)
            {
                return new MemberSearchResult(null, property, structDef);
            }

            if (string.IsNullOrEmpty(structDef.BaseStructName))
            {
                break;
            }

            CompilationUnitNode unit = _typeRepository.GetCompilationUnitForStruct(currentStructFqn);
            SimpleTypeNode baseTypeNode = new(new(TokenType.Identifier, structDef.BaseStructName, -1, -1));
            currentStructFqn = _typeResolver.ResolveType(baseTypeNode, structDef.Namespace, unit);
        }

        diagnostics.Add(new(
            context.CompilationUnit.FilePath,
            $"Type '{baseStructFqn}' has no member or property named '{ma.Member.Value}'.",
            ma.Member.Line,
            ma.Member.Column));

        return null;
    }

    private string AnalyzeFoundMember(MemberVariableNode member,
        StructDefinitionNode definingStruct,
        string baseStructType,
        MemberAccessExpressionNode ma,
        AnalysisContext context,
        List<Diagnostic> diagnostics)
    {
        if (member.AccessLevel == AccessSpecifier.Private)
        {
            string? ownerStructFqn = _typeRepository.GetFullyQualifiedOwnerName(context.CurrentFunction);
            
            if (ownerStructFqn != TypeRepository.GetFullyQualifiedName(definingStruct))
            {
                diagnostics.Add(new(
                   context.CompilationUnit.FilePath,
                   $"Member '{definingStruct.Name}::{member.Name.Value}' is private and cannot be accessed from this context.",
                   ma.Member.Line,
                   ma.Member.Column
               ));
            }
        }

        (int _, string resolvedMemberType) = _memoryLayoutManager.GetMemberInfo(
            baseStructType,
            ma.Member.Value,
            context.CompilationUnit);

        return resolvedMemberType;
    }

    private string AnalyzeFoundProperty(PropertyDefinitionNode property,
        StructDefinitionNode definingStruct,
        MemberAccessExpressionNode ma,
        AnalysisContext context,
        List<Diagnostic> diagnostics)
    {
        if (property.AccessLevel == AccessSpecifier.Private)
        {
            string? ownerStructFqn = _typeRepository.GetFullyQualifiedOwnerName(context.CurrentFunction);
            
            if (ownerStructFqn != TypeRepository.GetFullyQualifiedName(definingStruct))
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

        if (isLValue)
        {
            if (!property.Accessors.Any(a => a.AccessorKeyword.Value == "set"))
            {
                diagnostics.Add(new(
                    context.CompilationUnit.FilePath,
                    $"Property '{property.Name.Value}' does not have a 'set' accessor.",
                    ma.Member.Line,
                    ma.Member.Column));
            }
        }
        else
        {
            if (!property.Accessors.Any(a => a.AccessorKeyword.Value == "get"))
            {
                diagnostics.Add(new Diagnostic(context.CompilationUnit.FilePath, $"Property '{property.Name.Value}' does not have a 'get' accessor.", ma.Member.Line, ma.Member.Column));
            }
        }

        return _typeResolver.ResolveType(property.Type, definingStruct.Namespace, context.CompilationUnit);
    }
}