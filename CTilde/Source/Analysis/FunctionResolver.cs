using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class FunctionResolver
{
    private readonly TypeRepository _typeRepository;
    private readonly TypeResolver _typeResolver;
    private readonly ProgramNode _program; // For accessing all functions

    public FunctionResolver(TypeRepository typeRepository, TypeResolver typeResolver, ProgramNode program)
    {
        _typeRepository = typeRepository;
        _typeResolver = typeResolver;
        _program = program;
    }

    public FunctionDeclarationNode ResolveFunctionCall(ExpressionNode callee, CompilationUnitNode context, FunctionDeclarationNode? currentFunction)
    {
        if (callee is VariableExpressionNode varNode) return ResolveFunctionByName(varNode.Identifier.Value, currentFunction?.Namespace, context);
        if (callee is QualifiedAccessExpressionNode qNode)
        {
            string qualifier = TypeResolver.ResolveQualifier(qNode.Left);
            var funcName = qNode.Member.Value;
            string? resolvedNamespace = qualifier;
            var aliased = context.Usings.FirstOrDefault(u => u.Alias == qualifier);
            if (aliased != null) resolvedNamespace = aliased.Namespace;

            var globalFunctions = _program.CompilationUnits.SelectMany(cu => cu.Functions);
            var func = globalFunctions.FirstOrDefault(f => f.OwnerStructName == null && f.Namespace == resolvedNamespace && f.Name == funcName);
            if (func == null) throw new InvalidOperationException($"Function '{resolvedNamespace}::{funcName}' not found.");
            return func;
        }
        throw new NotSupportedException($"Unsupported callee type for resolution: {callee.GetType().Name}");
    }

    private FunctionDeclarationNode ResolveFunctionByName(string name, string? currentNamespace, CompilationUnitNode context)
    {
        var globalFunctions = _program.CompilationUnits.SelectMany(cu => cu.Functions);
        var candidates = globalFunctions.Where(f => f.OwnerStructName == null && f.Name == name)
            .Where(f => f.Namespace == currentNamespace || f.Namespace == null || context.Usings.Any(u => u.Alias == null && u.Namespace == f.Namespace)).ToList();
        if (candidates.Count == 0) throw new InvalidOperationException($"Function '{name}' could not be resolved in the current context.");
        if (candidates.Select(f => f.Namespace).Distinct().Count() > 1) throw new InvalidOperationException($"Function call '{name}' is ambiguous.");
        return candidates.First();
    }

    public FunctionDeclarationNode? ResolveMethod(string ownerFqn, string name)
    {
        var structFqn = ownerFqn;
        while (structFqn != null)
        {
            var structDef = _typeRepository.FindStruct(structFqn);
            if (structDef == null) return null; // Should not happen if ownerFqn is valid

            var method = structDef.Methods.FirstOrDefault(m => m.Name == name);
            if (method != null) return method;

            if (string.IsNullOrEmpty(structDef.BaseStructName)) break;

            var unit = _typeRepository.GetCompilationUnitForStruct(structFqn);
            structFqn = _typeResolver.ResolveTypeName(structDef.BaseStructName, structDef.Namespace, unit);
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
        if (structDef == null) return null;

        var ctorUnit = _typeRepository.GetCompilationUnitForStruct(structFqn);

        foreach (var ctor in structDef.Constructors)
        {
            if (ctor.Parameters.Count != argTypeFqns.Count) continue;

            bool allParamsMatch = true;
            for (int i = 0; i < argTypeFqns.Count; i++)
            {
                var param = ctor.Parameters[i];
                var rawParamType = TypeRepository.GetTypeNameFromToken(param.Type, param.PointerLevel);
                var baseParamType = rawParamType.TrimEnd('*');
                var pointerSuffix = new string('*', rawParamType.Length - baseParamType.Length);

                string resolvedParamType;
                if (param.Type.Type == TokenType.Keyword || baseParamType.Equals("void", StringComparison.OrdinalIgnoreCase))
                {
                    resolvedParamType = rawParamType;
                }
                else
                {
                    resolvedParamType = _typeResolver.ResolveTypeName(baseParamType, ctor.Namespace, ctorUnit) + pointerSuffix;
                }

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
            .Concat(context.Usings.Where(u => u.Alias == null).Select(u => u.Namespace))
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