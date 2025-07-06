using CTilde.Diagnostics;

namespace CTilde.Analysis.ExpressionAnalyzers;

public class VariableExpressionAnalyzer : ExpressionAnalyzerBase
{
    private record ImplicitMemberSearchResult(MemberVariableNode Member, StructDefinitionNode DefiningStruct);

    public VariableExpressionAnalyzer(
        SemanticAnalyzer semanticAnalyzer,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        MemoryLayoutManager memoryLayoutManager)
        : base(semanticAnalyzer, typeRepository, typeResolver, functionResolver, memoryLayoutManager) { }

    public override string Analyze(ExpressionNode expr, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        var v = (VariableExpressionNode)expr;

        if (context.Symbols.TryGetSymbol(v.Identifier.Value, out _, out var type, out _))
        {
            context.Symbols.MarkAsRead(v.Identifier.Value);
            return type;
        }

        int? unqualifiedEnumValue = _functionResolver.ResolveUnqualifiedEnumMember(
            v.Identifier.Value,
            context.CompilationUnit,
            context.CurrentFunction?.Namespace);
        
        if (unqualifiedEnumValue.HasValue)
        {
            return "int";
        }

        string? implicitMemberType = AnalyzeAsImplicitThisMember(v, context, diagnostics);
        
        if (implicitMemberType is not null)
        {
            return implicitMemberType;
        }

        diagnostics.Add(new(
            context.CompilationUnit.FilePath,
            $"Cannot determine type for undefined variable '{v.Identifier.Value}'.",
            v.Identifier.Line,
            v.Identifier.Column));

        return "unknown";
    }

    private string? AnalyzeAsImplicitThisMember(VariableExpressionNode v, AnalysisContext context, List<Diagnostic> diagnostics)
    {
        if (context.CurrentFunction?.OwnerStructName is null)
        {
            return null;
        }

        string ownerStructFqn = _typeRepository.GetFullyQualifiedOwnerName(context.CurrentFunction)!;
        ImplicitMemberSearchResult? searchResult = FindImplicitMemberInHierarchy(v.Identifier.Value, ownerStructFqn, context);

        if (searchResult is null)
        {
            return null;
        }

        context.Symbols.MarkAsRead("this");

        if (searchResult.Member.AccessLevel == AccessSpecifier.Private)
        {
            string definingStructFqn = TypeRepository.GetFullyQualifiedName(searchResult.DefiningStruct);
            string currentFunctionOwnerFqn = _typeRepository.GetFullyQualifiedOwnerName(context.CurrentFunction)!;

            if (definingStructFqn != currentFunctionOwnerFqn)
            {
                diagnostics.Add(new(
                    context.CompilationUnit.FilePath,
                    $"Member '{searchResult.DefiningStruct.Name}::{searchResult.Member.Name.Value}' is private and cannot be accessed from this context.",
                    v.Identifier.Line,
                    v.Identifier.Column
                ));
            }
        }

        (int _, string memberTypeResolved) = _memoryLayoutManager.GetMemberInfo(ownerStructFqn, v.Identifier.Value, context.CompilationUnit);
        
        return memberTypeResolved;
    }

    private ImplicitMemberSearchResult? FindImplicitMemberInHierarchy(string memberName, string ownerStructFqn, AnalysisContext context)
    {
        string? currentStructFqn = ownerStructFqn;
        
        while (currentStructFqn is not null)
        {
            StructDefinitionNode? structDef = _typeRepository.FindStruct(currentStructFqn);
            
            if (structDef is null)
            {
                break;
            }

            MemberVariableNode? member = structDef.Members.FirstOrDefault(m => m.Name.Value == memberName);
            
            if (member is not null)
            {
                return new(member, structDef);
            }

            if (string.IsNullOrEmpty(structDef.BaseStructName))
            {
                break;
            }

            CompilationUnitNode unit = _typeRepository.GetCompilationUnitForStruct(currentStructFqn);
            SimpleTypeNode baseTypeNode = new(new(TokenType.Identifier, structDef.BaseStructName, -1, -1));
            currentStructFqn = _typeResolver.ResolveType(baseTypeNode, structDef.Namespace, unit);
        }

        return null;
    }
}