namespace CTilde;

public class Monomorphizer
{
    private readonly TypeRepository _typeRepository;
    private TypeResolver _typeResolver = null!;
    private readonly Dictionary<string, StructDefinitionNode> _instantiationCache = new();

    public Monomorphizer(TypeRepository typeRepository)
    {
        _typeRepository = typeRepository;
    }

    public void SetResolver(TypeResolver resolver)
    {
        _typeResolver = resolver;
    }

    public StructDefinitionNode Instantiate(GenericInstantiationTypeNode typeNode, string? currentNamespace, CompilationUnitNode contextForResolution)
    {
        (StructDefinitionNode templateStruct, List<TypeNode> resolvedArgNodes, CompilationUnitNode templateUnit, string templateFqn) =
            ResolveTemplateAndArguments(typeNode, currentNamespace, contextForResolution);

        string mangledName = NameMangler.MangleGenericInstance(templateFqn, resolvedArgNodes);
        if (_instantiationCache.TryGetValue(mangledName, out var cachedStruct))
        {
            return cachedStruct;
        }

        Dictionary<string, TypeNode> replacements = CreateReplacementMap(templateStruct, resolvedArgNodes, templateFqn);
        StructDefinitionNode concreteStruct = CloneAndAdaptStruct(templateStruct, replacements, mangledName);

        RegisterAndFinalizeNewStruct(concreteStruct, templateUnit);

        _instantiationCache[mangledName] = concreteStruct;
        return concreteStruct;
    }

    private (StructDefinitionNode TemplateStruct, List<TypeNode> ResolvedArgs, CompilationUnitNode TemplateUnit, string TemplateFqn)
        ResolveTemplateAndArguments(GenericInstantiationTypeNode typeNode, string? currentNamespace, CompilationUnitNode context)
    {
        string templateFqn = _typeResolver.ResolveSimpleTypeName(typeNode.BaseType.Value, currentNamespace, context);
        
        StructDefinitionNode templateStruct = _typeRepository.FindStruct(templateFqn)
            ?? throw new InvalidOperationException($"Generic template '{templateFqn}' not found.");
        
        CompilationUnitNode templateUnit = _typeRepository.GetCompilationUnitForStruct(templateFqn);

        List<TypeNode> resolvedArgNodes = typeNode.TypeArguments.Select(arg =>
        {
            string argFqn = _typeResolver.ResolveType(arg, currentNamespace, context);
            return FqnToTypeNode(argFqn);
        }).ToList();

        return (templateStruct, resolvedArgNodes, templateUnit, templateFqn);
    }

    private static TypeNode FqnToTypeNode(string fqn)
    {
        int pointerLevel = fqn.Count(c => c == '*');
        string baseNameWithNamespace = fqn.TrimEnd('*');
        string baseName = baseNameWithNamespace.Split("::").Last();

        TypeNode resolvedNode = new SimpleTypeNode(new Token(TokenType.Identifier, baseName, -1, -1));
        
        for (int i = 0; i < pointerLevel; i++)
        {
            resolvedNode = new PointerTypeNode(resolvedNode);
        }

        return resolvedNode;
    }

    private static Dictionary<string, TypeNode> CreateReplacementMap(StructDefinitionNode templateStruct, List<TypeNode> resolvedArgNodes, string templateFqn)
    {
        if (templateStruct.GenericParameters.Count != resolvedArgNodes.Count)
        {
            throw new InvalidOperationException($"Incorrect number of type arguments for generic type '{templateFqn}'.");
        }

        return templateStruct.GenericParameters
            .Select((p, i) => new { ParamName = p.Value, ConcreteType = resolvedArgNodes[i] })
            .ToDictionary(pair => pair.ParamName, pair => pair.ConcreteType);
    }

    private static StructDefinitionNode CloneAndAdaptStruct(StructDefinitionNode templateStruct, Dictionary<string, TypeNode> replacements, string mangledName)
    {
        AstCloner cloner = new(replacements);
        StructDefinitionNode clonedStruct = cloner.Clone(templateStruct);

        List<FunctionDeclarationNode> updatedMethods = clonedStruct.Methods.Select(m =>
        {
            ParameterNode thisParam = m.Parameters.First();
            PointerTypeNode newThisType = new(new SimpleTypeNode(new(TokenType.Identifier, mangledName, -1, -1)));
            ParameterNode newThisParam = thisParam with { Type = newThisType };
            List<ParameterNode> newParams = new List<ParameterNode> { newThisParam }.Concat(m.Parameters.Skip(1)).ToList();

            return m with 
            { 
                OwnerStructName = mangledName, 
                Namespace = null, 
                Parameters = newParams 
            };
        }).ToList();

        List<ConstructorDeclarationNode> updatedConstructors = clonedStruct.Constructors
            .Select(c => c with { OwnerStructName = mangledName, Namespace = null })
            .ToList();

        List<DestructorDeclarationNode> updatedDestructors = clonedStruct.Destructors
            .Select(d => d with { OwnerStructName = mangledName, Namespace = null })
            .ToList();

        return clonedStruct with
        {
            Name = mangledName,
            Namespace = null,
            GenericParameters = [],
            Methods = updatedMethods,
            Constructors = updatedConstructors,
            Destructors = updatedDestructors
        };
    }

    private void RegisterAndFinalizeNewStruct(StructDefinitionNode concreteStruct, CompilationUnitNode templateUnit)
    {
        _typeRepository.RegisterInstantiatedStruct(concreteStruct, templateUnit);
        templateUnit.Structs.Add(concreteStruct);

        Parser parser = new([]);
        parser.SetParents(concreteStruct, templateUnit);
    }
}