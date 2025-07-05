namespace CTilde;

public class Monomorphizer
{
    private readonly TypeRepository _typeRepository;
    private TypeResolver _typeResolver = null!; // Set via SetResolver to break circular dependency
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
        // 1. Resolve the base generic type (e.g., 'List' -> 'std::List')
        var templateFqn = _typeResolver.ResolveSimpleTypeName(typeNode.BaseType.Value, currentNamespace, contextForResolution);
        var templateStruct = _typeRepository.FindStruct(templateFqn) ?? throw new System.InvalidOperationException($"Generic template '{templateFqn}' not found.");
        var templateUnit = _typeRepository.GetCompilationUnitForStruct(templateFqn);

        // 2. Resolve all type arguments
        var resolvedArgNodes = typeNode.TypeArguments.Select(arg =>
        {
            // Recursively resolve the type argument itself. This supports things like List<List<int>>.
            var argFqn = _typeResolver.ResolveType(arg, currentNamespace, contextForResolution);

            // We need a TypeNode representation of the resolved FQN.
            // This is a bit of a hack, but it works for now.
            var lastPart = argFqn.Split("::").Last();
            var pointerLevel = argFqn.Count(c => c == '*');
            var baseName = lastPart.TrimEnd('*');
            TypeNode resolvedArgNode = new SimpleTypeNode(new Token(TokenType.Identifier, baseName, -1, -1));
            for (int i = 0; i < pointerLevel; i++)
            {
                resolvedArgNode = new PointerTypeNode(resolvedArgNode);
            }
            return resolvedArgNode;

        }).ToList();

        // 3. Generate mangled name and check cache
        var mangledName = NameMangler.MangleGenericInstance(templateFqn, resolvedArgNodes);
        if (_instantiationCache.TryGetValue(mangledName, out var cachedStruct))
        {
            return cachedStruct;
        }

        // 4. Create replacement map for the cloner
        if (templateStruct.GenericParameters.Count != resolvedArgNodes.Count)
        {
            throw new System.InvalidOperationException($"Incorrect number of type arguments for generic type '{templateFqn}'.");
        }
        var replacements = new Dictionary<string, TypeNode>();
        for (int i = 0; i < templateStruct.GenericParameters.Count; i++)
        {
            var paramName = templateStruct.GenericParameters[i].Value;
            var concreteType = resolvedArgNodes[i];
            replacements[paramName] = concreteType;
        }

        // 5. Clone the template's AST, substituting generic parameters
        var cloner = new AstCloner(replacements);
        var clonedStruct = cloner.Clone(templateStruct);

        // 6. Update the cloned struct's name and clear its generic parameters and namespace.
        // The mangled name is now the FQN.
        var concreteStruct = clonedStruct with
        {
            Name = mangledName,
            Namespace = null,
            GenericParameters = new List<Token>()
        };

        // 6a. Update the owner name, namespace, and `this` parameter type on all nested members.
        var updatedMethods = concreteStruct.Methods.Select(m =>
        {
            // The first parameter of a method is always 'this'.
            var thisParam = m.Parameters.First();
            var newThisType = new PointerTypeNode(new SimpleTypeNode(new Token(TokenType.Identifier, mangledName, -1, -1)));
            var newThisParam = thisParam with { Type = newThisType };

            var newParams = new List<ParameterNode> { newThisParam };
            newParams.AddRange(m.Parameters.Skip(1));

            return m with
            {
                OwnerStructName = mangledName,
                Namespace = null,
                Parameters = newParams
            };
        }).ToList();

        var updatedConstructors = concreteStruct.Constructors
            .Select(c => c with { OwnerStructName = mangledName, Namespace = null })
            .ToList();
        var updatedDestructors = concreteStruct.Destructors
            .Select(d => d with { OwnerStructName = mangledName, Namespace = null })
            .ToList();

        concreteStruct = concreteStruct with
        {
            Methods = updatedMethods,
            Constructors = updatedConstructors,
            Destructors = updatedDestructors
        };


        // 7. Register the new struct with the TypeRepository and its compilation unit
        _typeRepository.RegisterInstantiatedStruct(concreteStruct, templateUnit);
        templateUnit.Structs.Add(concreteStruct); // Add to the original unit's list of structs

        // 8. Set parents for the new AST subtree
        var parser = new Parser(new List<Token>()); // Dummy parser for SetParents
        parser.SetParents(concreteStruct, templateUnit);


        // 9. Cache and return
        _instantiationCache[mangledName] = concreteStruct;
        return concreteStruct;
    }
}