using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class CodeGenerator
{
    internal ProgramNode Program { get; }
    internal TypeManager TypeManager { get; }
    internal SemanticAnalyzer SemanticAnalyzer { get; }
    internal AssemblyBuilder Builder { get; } = new();

    private int _labelIdCounter;
    private readonly Dictionary<string, string> _stringLiterals = new();
    internal HashSet<string> ExternalFunctions { get; } = new();

    private readonly StatementGenerator _statementGenerator;
    internal ExpressionGenerator ExpressionGenerator { get; }

    public CodeGenerator(ProgramNode program)
    {
        Program = program;
        TypeManager = new TypeManager(program);
        SemanticAnalyzer = new SemanticAnalyzer(TypeManager);

        ExpressionGenerator = new ExpressionGenerator(this);
        _statementGenerator = new StatementGenerator(this);
    }

    public string Generate()
    {
        FindAllStringLiterals(Program);

        foreach (var unit in Program.CompilationUnits)
            foreach (var f in unit.Functions.Where(f => f.Body == null))
            {
                ExternalFunctions.Add(f.Name);
            }

        var fasmWriter = new FasmWriter();
        fasmWriter.WritePreamble(Builder);

        GenerateVTables();
        fasmWriter.WriteDataSection(Builder, _stringLiterals);

        fasmWriter.WriteTextSectionHeader(Builder);
        fasmWriter.WriteEntryPoint(Builder);

        foreach (var unit in Program.CompilationUnits)
        {
            foreach (var function in unit.Functions.Where(f => f.Body != null))
            {
                GenerateFunction(function, unit, null);
                Builder.AppendBlankLine();
            }

            foreach (var s in unit.Structs)
            {
                foreach (var method in s.Methods.Where(m => m.Body != null))
                {
                    GenerateFunction(method, unit, s);
                    Builder.AppendBlankLine();
                }
                foreach (var ctor in s.Constructors)
                {
                    GenerateConstructor(ctor, unit);
                    Builder.AppendBlankLine();
                }
                foreach (var dtor in s.Destructors)
                {
                    GenerateDestructor(dtor, unit);
                    Builder.AppendBlankLine();
                }
            }
        }

        fasmWriter.WriteImportDataSection(Builder, Program, ExternalFunctions);

        return Builder.ToString();
    }

    private void GenerateVTables()
    {
        Builder.AppendDirective("section '.rdata' data readable");
        foreach (var s in Program.CompilationUnits.SelectMany(cu => cu.Structs))
        {
            var structFqn = TypeManager.GetFullyQualifiedName(s);
            if (TypeManager.HasVTable(structFqn))
            {
                Builder.AppendLabel(TypeManager.GetVTableLabel(s));
                var vtable = TypeManager.GetVTable(structFqn);
                foreach (var entry in vtable)
                {
                    var mangledName = entry switch
                    {
                        FunctionDeclarationNode f => TypeManager.Mangle(f),
                        DestructorDeclarationNode d => TypeManager.Mangle(d),
                        _ => throw new InvalidOperationException("Invalid vtable entry type")
                    };
                    Builder.AppendInstruction($"dd {mangledName}");
                }
                Builder.AppendBlankLine();
            }
        }
    }

    internal int GetNextLabelId() => _labelIdCounter++;

    private void FindAllStringLiterals(AstNode node)
    {
        if (node is StringLiteralNode str && !_stringLiterals.ContainsValue(str.Value))
        {
            _stringLiterals.Add(str.Label, str.Value);
        }

        foreach (var property in node.GetType().GetProperties())
        {
            if (property.Name == "Parent") continue;

            if (property.GetValue(node) is AstNode child)
            {
                FindAllStringLiterals(child);
            }
            else if (property.GetValue(node) is IEnumerable<AstNode> children)
            {
                foreach (var c in children) FindAllStringLiterals(c);
            }
        }
    }

    private void GenerateConstructor(ConstructorDeclarationNode ctor, CompilationUnitNode unit)
    {
        var symbols = new SymbolTable(ctor, TypeManager, unit);
        // Create a dummy function node to provide context for analysis, preventing NullReferenceException.
        var dummyFunctionForContext = new FunctionDeclarationNode(
            new Token(TokenType.Keyword, "void"), 0, ctor.OwnerStructName,
            ctor.Parameters, ctor.Body, ctor.OwnerStructName, ctor.AccessLevel,
            false, false, ctor.Namespace
        );
        var context = new AnalysisContext(symbols, unit, dummyFunctionForContext);

        Builder.AppendLabel(TypeManager.Mangle(ctor));
        GeneratePrologue(symbols);

        if (ctor.Initializer != null)
        {
            var ownerStruct = TypeManager.FindStructByUnqualifiedName(ctor.OwnerStructName, ctor.Namespace) ?? throw new InvalidOperationException("Owner struct not found");

            var argTypes = ctor.Initializer.Arguments
                .Select(arg => SemanticAnalyzer.AnalyzeExpressionType(arg, context))
                .ToList();
            var baseFqn = TypeManager.ResolveTypeName(ownerStruct.BaseStructName!, ownerStruct.Namespace, unit);
            var baseCtor = TypeManager.FindConstructor(baseFqn, argTypes) ?? throw new InvalidOperationException("Base constructor not found for given argument types.");


            int totalArgSize = 0;
            foreach (var arg in ctor.Initializer.Arguments.AsEnumerable().Reverse())
            {
                totalArgSize += ExpressionGenerator.PushArgument(arg, context);
            }

            context.Symbols.TryGetSymbol("this", out var thisOffset, out _, out _);
            Builder.AppendInstruction($"mov eax, [ebp + {thisOffset}]", "Get 'this' pointer");
            Builder.AppendInstruction("push eax", "Push 'this' for base ctor");
            totalArgSize += 4;

            Builder.AppendInstruction($"call {TypeManager.Mangle(baseCtor)}");
            Builder.AppendInstruction($"add esp, {totalArgSize}", "Clean up base ctor args");
            Builder.AppendBlankLine();
        }

        _statementGenerator.GenerateStatement(ctor.Body, context);
        GenerateEpilogue(new List<(string, int, string)>());
    }

    private void GenerateDestructor(DestructorDeclarationNode dtor, CompilationUnitNode unit)
    {
        var symbols = new SymbolTable(dtor, TypeManager, unit);
        // Create a dummy function node to provide context for analysis.
        var dummyFunctionForContext = new FunctionDeclarationNode(
            new Token(TokenType.Keyword, "void"), 0, dtor.OwnerStructName,
            new List<ParameterNode>(), dtor.Body, dtor.OwnerStructName, dtor.AccessLevel,
            dtor.IsVirtual, false, dtor.Namespace
        );
        var context = new AnalysisContext(symbols, unit, dummyFunctionForContext);

        Builder.AppendLabel(TypeManager.Mangle(dtor));
        GeneratePrologue(symbols);
        _statementGenerator.GenerateStatement(dtor.Body, context);
        GenerateEpilogue(new List<(string, int, string)>());
    }

    private void GenerateFunction(FunctionDeclarationNode function, CompilationUnitNode unit, StructDefinitionNode? owner)
    {
        var symbols = new SymbolTable(function, TypeManager, unit);
        var context = new AnalysisContext(symbols, unit, function);
        var destructibleLocals = symbols.GetDestructibleLocals(TypeManager);

        string mangledName = function.Name == "main" ? "_main" : TypeManager.Mangle(function);

        Builder.AppendLabel(mangledName);
        GeneratePrologue(symbols);

        if (function.Body != null) _statementGenerator.GenerateStatement(function.Body, context);

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
                var dtor = TypeManager.FindDestructor(type);
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
                        Builder.AppendInstruction($"call {TypeManager.Mangle(dtor)}");
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