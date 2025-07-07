using System.Collections.Generic;
using CTilde.Analysis;
using CTilde.Diagnostics;

namespace CTilde;

public class Compilation
{
    public string EntryFilePath { get; }
    public OptimizationOptions Options { get; }

    public List<Diagnostic> Diagnostics { get; } = new();
    public Dictionary<string, string[]> SourceFileCache { get; } = new();
    public List<string> AllFiles { get; set; } = new();
    public ProgramNode? ProgramNode { get; set; }

    // Analysis Services
    public TypeRepository? TypeRepository { get; set; }
    public TypeResolver? TypeResolver { get; set; }
    public VTableManager? VTableManager { get; set; }
    public MemoryLayoutManager? MemoryLayoutManager { get; set; }
    public FunctionResolver? FunctionResolver { get; set; }
    public SemanticAnalyzer? SemanticAnalyzer { get; set; }

    public Compilation(string entryFilePath, OptimizationOptions options)
    {
        EntryFilePath = entryFilePath;
        Options = options;
    }
}