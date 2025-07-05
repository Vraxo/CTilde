using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde;

public class CodeGenerator
{
    internal ProgramNode Program { get; }
    internal TypeRepository TypeRepository { get; }
    internal TypeResolver TypeResolver { get; }
    internal FunctionResolver FunctionResolver { get; }
    internal VTableManager VTableManager { get; }
    internal MemoryLayoutManager MemoryLayoutManager { get; }
    internal SemanticAnalyzer SemanticAnalyzer { get; }
    internal AssemblyBuilder Builder { get; } = new();

    private int _labelIdCounter;
    private readonly Dictionary<string, string> _stringLiterals = new();
    internal HashSet<string> ExternalFunctions { get; } = new();

    internal StatementGenerator StatementGenerator { get; }
    internal ExpressionGenerator ExpressionGenerator { get; }
    private readonly DeclarationGenerator _declarationGenerator;

    public CodeGenerator(
        ProgramNode program,
        TypeRepository typeRepository,
        TypeResolver typeResolver,
        FunctionResolver functionResolver,
        VTableManager vtableManager,
        MemoryLayoutManager memoryLayoutManager,
        SemanticAnalyzer semanticAnalyzer)
    {
        Program = program;
        TypeRepository = typeRepository;
        TypeResolver = typeResolver;
        FunctionResolver = functionResolver;
        VTableManager = vtableManager;
        MemoryLayoutManager = memoryLayoutManager;
        SemanticAnalyzer = semanticAnalyzer;

        ExpressionGenerator = new ExpressionGenerator(this);
        StatementGenerator = new StatementGenerator(this);
        _declarationGenerator = new DeclarationGenerator(this);
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
                _declarationGenerator.GenerateFunction(function, unit, null);
                Builder.AppendBlankLine();
            }

            foreach (var s in unit.Structs)
            {
                foreach (var method in s.Methods.Where(m => m.Body != null))
                {
                    _declarationGenerator.GenerateFunction(method, unit, s);
                    Builder.AppendBlankLine();
                }
                foreach (var ctor in s.Constructors)
                {
                    _declarationGenerator.GenerateConstructor(ctor, unit);
                    Builder.AppendBlankLine();
                }
                foreach (var dtor in s.Destructors)
                {
                    _declarationGenerator.GenerateDestructor(dtor, unit);
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
            var structFqn = TypeRepository.GetFullyQualifiedName(s);
            if (VTableManager.HasVTable(structFqn))
            {
                Builder.AppendLabel(NameMangler.GetVTableLabel(structFqn));
                var vtable = VTableManager.GetVTable(structFqn);
                foreach (var entry in vtable)
                {
                    var mangledName = entry switch
                    {
                        FunctionDeclarationNode f => NameMangler.Mangle(f),
                        DestructorDeclarationNode d => NameMangler.Mangle(d),
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
}