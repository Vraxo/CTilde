namespace CTilde;

public class DeclarationGenerator
{
    private readonly CodeGenerator _context;
    private AssemblyBuilder Builder => _context.Builder;
    private TypeRepository TypeRepository => _context.TypeRepository;
    private TypeResolver TypeResolver => _context.TypeResolver;
    private FunctionResolver FunctionResolver => _context.FunctionResolver;
    private VTableManager VTableManager => _context.VTableManager;
    private MemoryLayoutManager MemoryLayoutManager => _context.MemoryLayoutManager;
    private SemanticAnalyzer SemanticAnalyzer => _context.SemanticAnalyzer;
    private StatementGenerator StatementGenerator => _context.StatementGenerator;
    private ExpressionGenerator ExpressionGenerator => _context.ExpressionGenerator;

    public DeclarationGenerator(CodeGenerator context)
    {
        _context = context;
    }

    public void GenerateConstructor(ConstructorDeclarationNode ctor, CompilationUnitNode unit)
    {
        var symbols = new SymbolTable(ctor, TypeResolver, FunctionResolver, MemoryLayoutManager, unit);
        // Create a dummy function node to provide context for analysis, preventing NullReferenceException.
        var dummyFunctionForContext = new FunctionDeclarationNode(
            new SimpleTypeNode(new Token(TokenType.Keyword, "void", -1, -1)), ctor.OwnerStructName,
            ctor.Parameters, ctor.Body, ctor.OwnerStructName, ctor.AccessLevel,
            false, false, ctor.Namespace
        );
        var analysisContext = new AnalysisContext(symbols, unit, dummyFunctionForContext);

        Builder.AppendLabel(NameMangler.Mangle(ctor));
        GeneratePrologue(symbols);

        if (ctor.Initializer != null)
        {
            var ownerStruct = TypeRepository.FindStructByUnqualifiedName(ctor.OwnerStructName, ctor.Namespace) ?? throw new InvalidOperationException("Owner struct not found");

            var argTypes = ctor.Initializer.Arguments
                .Select(arg => SemanticAnalyzer.AnalyzeExpressionType(arg, analysisContext))
                .ToList();
            var baseTypeNode = new SimpleTypeNode(new Token(TokenType.Identifier, ownerStruct.BaseStructName!, -1, -1));
            var baseFqn = TypeResolver.ResolveType(baseTypeNode, ownerStruct.Namespace, unit);
            var baseCtor = FunctionResolver.FindConstructor(baseFqn, argTypes) ?? throw new InvalidOperationException("Base constructor not found for given argument types.");


            int totalArgSize = 0;
            foreach (var arg in ctor.Initializer.Arguments.AsEnumerable().Reverse())
            {
                totalArgSize += ExpressionGenerator.PushArgument(arg, analysisContext);
            }

            symbols.TryGetSymbol("this", out var thisOffset, out _, out _);
            Builder.AppendInstruction($"mov eax, [ebp + {thisOffset}]", "Get 'this' pointer");
            Builder.AppendInstruction("push eax", "Push 'this' for base ctor");
            totalArgSize += 4;

            Builder.AppendInstruction($"call {NameMangler.Mangle(baseCtor)}");
            Builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up base ctor args");
            Builder.AppendBlankLine();
        }

        StatementGenerator.GenerateStatement(ctor.Body, analysisContext);
        GenerateEpilogue(new List<(string, int, string)>());
    }

    public void GenerateDestructor(DestructorDeclarationNode dtor, CompilationUnitNode unit)
    {
        var symbols = new SymbolTable(dtor, TypeResolver, FunctionResolver, MemoryLayoutManager, unit);
        // Create a dummy function node to provide context for analysis.
        var dummyFunctionForContext = new FunctionDeclarationNode(
            new SimpleTypeNode(new Token(TokenType.Keyword, "void", -1, -1)), dtor.OwnerStructName,
            new List<ParameterNode>(), dtor.Body, dtor.OwnerStructName, dtor.AccessLevel,
            dtor.IsVirtual, false, dtor.Namespace
        );
        var context = new AnalysisContext(symbols, unit, dummyFunctionForContext);

        Builder.AppendLabel(NameMangler.Mangle(dtor));
        GeneratePrologue(symbols);
        StatementGenerator.GenerateStatement(dtor.Body, context);
        GenerateEpilogue(new List<(string, int, string)>());
    }

    public void GenerateFunction(FunctionDeclarationNode function, CompilationUnitNode unit, StructDefinitionNode? owner)
    {
        var tempContext = new AnalysisContext(null, unit, function);
        var returnTypeFqn = SemanticAnalyzer.AnalyzeFunctionReturnType(function, tempContext);
        var returnsStructByValue = TypeRepository.IsStruct(returnTypeFqn) && !returnTypeFqn.EndsWith("*");

        var parametersWithRetPtr = new List<ParameterNode>(function.Parameters);
        if (returnsStructByValue)
        {
            var retPtrType = new PointerTypeNode(new SimpleTypeNode(new Token(TokenType.Keyword, "void", -1, -1)));
            var retPtrParam = new ParameterNode(retPtrType, new Token(TokenType.Identifier, "__ret_ptr", -1, -1));
            parametersWithRetPtr.Add(retPtrParam);
        }

        var functionForSymbols = function with { Parameters = parametersWithRetPtr };
        var symbols = new SymbolTable(functionForSymbols, TypeResolver, FunctionResolver, MemoryLayoutManager, unit);

        var context = new AnalysisContext(symbols, unit, function);
        var destructibleLocals = symbols.GetDestructibleLocals(FunctionResolver);

        string mangledName = function.Name == "main" ? "_main" : NameMangler.Mangle(function);

        Builder.AppendLabel(mangledName);
        GeneratePrologue(symbols);

        if (function.Body != null) StatementGenerator.GenerateStatement(function.Body, context);

        GenerateEpilogue(destructibleLocals);
    }

    private void GeneratePrologue(SymbolTable symbols)
    {
        Builder.AppendInstruction("push ebp");
        Builder.AppendInstruction("mov ebp, esp");
        Builder.AppendInstruction("push ebx", "Preserve non-volatile registers");
        Builder.AppendInstruction("push esi");
        Builder.AppendInstruction("push edi");
        Builder.AppendBlankLine();

        int totalLocalSize = symbols.TotalLocalSize;
        if (totalLocalSize > 0)
        {
            Builder.AppendInstruction($"sub esp, {totalLocalSize}", "Allocate space for all local variables");
        }
    }

    private void GenerateEpilogue(List<(string Name, int Offset, string TypeFqn)> destructibleLocals)
    {
        if (destructibleLocals.Any())
        {
            Builder.AppendBlankLine();
            Builder.AppendInstruction(null, "Destructor cleanup");
            foreach (var (name, offset, type) in destructibleLocals.AsEnumerable().Reverse())
            {
                var dtor = FunctionResolver.FindDestructor(type);
                if (dtor != null)
                {
                    Builder.AppendInstruction($"lea eax, [ebp + {offset}]", $"Get address of '{name}' for dtor");
                    Builder.AppendInstruction("push eax");

                    if (dtor.IsVirtual)
                    {
                        // The destructor is always at index 0 in the vtable if virtual
                        Builder.AppendInstruction("mov eax, [eax]", "Get vtable ptr");
                        Builder.AppendInstruction("mov eax, [eax]", "Get dtor from vtable[0]");
                        Builder.AppendInstruction("call eax");
                    }
                    else
                    {
                        Builder.AppendInstruction($"call {NameMangler.Mangle(dtor)}");
                    }
                    Builder.AppendInstruction("add esp, 4", "Clean up 'this'");
                }
            }
        }

        Builder.AppendBlankLine();
        Builder.AppendInstruction("pop edi");
        Builder.AppendInstruction("pop esi");
        Builder.AppendInstruction("pop ebx");
        Builder.AppendInstruction("mov esp, ebp");
        Builder.AppendInstruction("pop ebp");
        Builder.AppendInstruction("ret");
    }
}