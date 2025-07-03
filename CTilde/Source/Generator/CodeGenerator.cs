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

    internal SymbolTable CurrentSymbols { get; set; } = null!;
    internal CompilationUnitNode CurrentCompilationUnit { get; set; } = null!;
    internal FunctionDeclarationNode CurrentFunction { get; set; } = null!; // New property

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
        fasmWriter.WriteDataSection(Builder, _stringLiterals);
        fasmWriter.WriteTextSectionHeader(Builder);
        fasmWriter.WriteEntryPoint(Builder);

        foreach (var unit in Program.CompilationUnits)
        {
            CurrentCompilationUnit = unit;
            foreach (var function in unit.Functions.Where(f => f.Body != null))
            {
                CurrentFunction = function; // Set current function
                GenerateFunction(function);
                Builder.AppendBlankLine();
            }
        }

        fasmWriter.WriteImportDataSection(Builder, Program, ExternalFunctions);

        return Builder.ToString();
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
                foreach (var c in children)
                {
                    FindAllStringLiterals(c);
                }
            }
        }
    }

    private void GenerateFunction(FunctionDeclarationNode function)
    {
        CurrentSymbols = new SymbolTable(function, TypeManager, CurrentCompilationUnit);

        string mangledName;
        if (function.Name == "main")
        {
            mangledName = "_main";
        }
        else
        {
            var nameParts = new List<string>();
            if (function.Namespace != null) nameParts.Add(function.Namespace);
            if (function.OwnerStructName != null) nameParts.Add(function.OwnerStructName);
            nameParts.Add(function.Name);
            mangledName = "_" + string.Join("_", nameParts);
        }

        Builder.AppendLabel(mangledName);
        Builder.AppendInstruction("push ebp");
        Builder.AppendInstruction("mov ebp, esp");
        Builder.AppendInstruction("push ebx", "Preserve non-volatile registers");
        Builder.AppendInstruction("push esi");
        Builder.AppendInstruction("push edi");
        Builder.AppendBlankLine();

        int totalLocalSize = CurrentSymbols.TotalLocalSize;
        if (totalLocalSize > 0)
        {
            Builder.AppendInstruction($"sub esp, {totalLocalSize}", "Allocate space for all local variables");
        }

        if (function.Body != null)
        {
            _statementGenerator.GenerateStatement(function.Body);
        }

        Builder.AppendBlankLine();
        Builder.AppendInstruction(null, "Implicit return cleanup");
        GenerateFunctionEpilogue();
    }

    internal void GenerateFunctionEpilogue()
    {
        Builder.AppendInstruction("pop edi");
        Builder.AppendInstruction("pop esi");
        Builder.AppendInstruction("pop ebx");
        Builder.AppendInstruction("mov esp, ebp");
        Builder.AppendInstruction("pop ebp");
        Builder.AppendInstruction("ret");
    }
}