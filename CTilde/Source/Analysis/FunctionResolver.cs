using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class FunctionResolver
{
    private readonly TypeRepository _typeRepository;
    private readonly TypeResolver _typeResolver;
    private readonly ProgramNode _program; // For accessing all functions
    private SemanticAnalyzer _semanticAnalyzer = null!;

    public FunctionResolver(TypeRepository typeRepository, TypeResolver typeResolver, ProgramNode program)
    {
        _typeRepository = typeRepository;
        _typeResolver = typeResolver;
        _program = program;
    }

    public void SetSemanticAnalyzer(SemanticAnalyzer analyzer) => _semanticAnalyzer = analyzer;

    public FunctionDeclarationNode ResolveFunctionCall(ExpressionNode callee, AnalysisContext analysisContext)
    {
        var currentFunction = analysisContext.CurrentFunction;
        var context = analysisContext.CompilationUnit;

        if (callee is MemberAccessExpressionNode ma)
        {
            var ownerTypeFqn = _semanticAnalyzer.AnalyzeExpressionType(ma.Left, analysisContext).TrimEnd('*');
            var method = ResolveMethod(ownerTypeFqn, ma.Member.Value);
            return method ?? throw new InvalidOperationException($"Method '{ma.Member.Value}' not found on type '{ownerTypeFqn}'.");
        }

        if (callee is VariableExpressionNode varNode)
        {
            // If inside a method, first try resolving as an implicit 'this' call.
            if (currentFunction?.OwnerStructName is not null)
            {
                var ownerFqn = _typeRepository.GetFullyQualifiedOwnerName(currentFunction);
                if (ownerFqn is not null)
                {
                    var method = ResolveMethod(ownerFqn, varNode.Identifier.Value);
                    if (method is not null)
                    {
                        return method;
                    }
                }
            }
            // Fallback to global/namespaced function resolution.
            return ResolveFunctionByName(varNode.Identifier.Value, currentFunction?.Namespace, context);
        }

        if (callee is QualifiedAccessExpressionNode qNode)
        {
            string qualifier = TypeResolver.ResolveQualifier(qNode.Left);
            var funcName = qNode.Member.Value;
            string? resolvedNamespace = qualifier;
            var aliased = context.Usings.FirstOrDefault(u => u.Alias == qualifier);
            if (aliased is not null) resolvedNamespace = aliased.Namespace;

            var globalFunctions = _program.CompilationUnits.SelectMany(cu => cu.Functions);
            var func = globalFunctions.FirstOrDefault(f => f.OwnerStructName is null && f.Namespace == resolvedNamespace && f.Name == funcName);
            if (func is null) throw new InvalidOperationException($"Function '{resolvedNamespace}::{funcName}' not found.");
            return func;
        }

        // --- ENHANCED DEBUGGING EXCEPTION ---
        string parentInfo = "null";
        if (callee.Parent is not null)
        {
            parentInfo = callee.Parent.GetType().Name;
            if (callee.Parent is CallExpressionNode callParent)
            {
                var calleeType = callParent.Callee.GetType().Name;
                var allArgs = string.Join(", ", callParent.Arguments.Select(a => a.GetType().Name));
                parentInfo += $" (Callee: {calleeType}, Args: [{allArgs}])";
            }
        }
        var token = AstHelper.GetFirstToken(callee);
        var detailedMessage = $"Unsupported callee type for resolution: {callee.GetType().Name} with value '{token.Value}'. Parent is {parentInfo}.";
        throw new InvalidOperationException(detailedMessage);
    }

    private FunctionDeclarationNode ResolveFunctionByName(string name, string? currentNamespace, CompilationUnitNode context)
    {
        var globalFunctions = _program.CompilationUnits.SelectMany(cu => cu.Functions);
        var candidates = globalFunctions.Where(f => f.OwnerStructName is null && f.Name == name)
            .Where(f => f.Namespace == currentNamespace || f.Namespace is null || context.Usings.Any(u => u.Alias is null && u.Namespace == f.Namespace)).ToList();
        if (candidates.Count == 0) throw new InvalidOperationException($"Function '{name}' could not be resolved in the current context.");
        if (candidates.Select(f => f.Namespace).Distinct().Count() > 1) throw new InvalidOperationException($"Function call '{name}' is ambiguous.");
        return candidates.First();
    }

    public FunctionDeclarationNode? ResolveMethod(string ownerFqn, string name)
    {
        var structFqn = ownerFqn;
        while (structFqn is not null)
        {
            var structDef = _typeRepository.FindStruct(structFqn);
            if (structDef is null) return null; // Should not happen if ownerFqn is valid

            var method = structDef.Methods.FirstOrDefault(m => m.Name == name);
            if (method is not null) return method;

            if (string.IsNullOrEmpty(structDef.BaseStructName)) break;

            var unit = _typeRepository.GetCompilationUnitForStruct(structFqn);
            var baseTypeNode = new SimpleTypeNode(new Token(TokenType.Identifier, structDef.BaseStructName, -1, -1));
            structFqn = _typeResolver.ResolveType(baseTypeNode, structDef.Namespace, unit);
        }
        return null;
    }

    public FunctionDeclarationNode? FindMethod(string structFqn, string methodName)
    {
        var structDef = _typeRepository.FindStruct(structFqn);
        return structDef?.Methods.FirstOrDefault(m => m.Name == methodName);
    }

    public ConstructorDeclarationNode? FindConstructor(string structFqn, List<string> argTypeFqns)
    {
        var structDef = _typeRepository.FindStruct(structFqn);
        if (structDef is null) return null;

        var ctorUnit = _typeRepository.GetCompilationUnitForStruct(structFqn);

        foreach (var ctor in structDef.Constructors)
        {
            if (ctor.Parameters.Count != argTypeFqns.Count) continue;

            bool allParamsMatch = true;
            for (int i = 0; i < argTypeFqns.Count; i++)
            {
                var param = ctor.Parameters[i];
                var resolvedParamType = _typeResolver.ResolveType(param.Type, ctor.Namespace, ctorUnit);

                string argumentType = argTypeFqns[i];
                bool isMatch = resolvedParamType == argumentType;

                if (!isMatch && resolvedParamType == "char" && argumentType == "int")
                {
                    isMatch = true;
                }

                // Allow assigning an int (from malloc) to any pointer type
                if (!isMatch && argumentType == "int" && resolvedParamType.EndsWith("*"))
                {
                    isMatch = true;
                }

                if (!isMatch)
                {
                    allParamsMatch = false;
                    break;
                }
            }

            if (allParamsMatch) return ctor;
        }

        return null;
    }

    public DestructorDeclarationNode? FindDestructor(string fqn)
    {
        var structDef = _typeRepository.FindStruct(fqn);
        return structDef?.Destructors.FirstOrDefault();
    }

    public int? GetEnumValue(string enumFQN, string memberName)
    {
        var ed = _typeRepository.FindEnum(enumFQN);
        return ed?.Members.FirstOrDefault(m => m.Name.Value == memberName)?.Value;
    }

    public int? ResolveUnqualifiedEnumMember(string memberName, CompilationUnitNode context, string? currentContextNamespace)
    {
        var namespacesToCheck = new List<string?> { currentContextNamespace }
            .Concat(context.Usings.Where(u => u.Alias is null).Select(u => u.Namespace))
            .Append(null);

        foreach (var ns in namespacesToCheck.Distinct())
        {
            foreach (var enumDef in _typeRepository.GetAllEnums().Where(e => e.Namespace == ns))
            {
                if (enumDef.Members.Any(m => m.Name.Value == memberName))
                {
                    return GetEnumValue(TypeRepository.GetFullyQualifiedName(enumDef), memberName);
                }
            }
        }
        return null;
    }
}