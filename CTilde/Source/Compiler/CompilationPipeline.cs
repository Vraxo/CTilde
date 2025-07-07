using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTilde.Analysis;
using CTilde.Diagnostics;

namespace CTilde;

public static class CompilationPipeline
{
    // Stage 1: Parsing
    public static bool RunParsingStage(Compilation compilation)
    {
        compilation.AllFiles = DiscoverDependencies(compilation.EntryFilePath);
        ParseIntoCompilationUnits(compilation);
        return !compilation.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    // Stage 2: Analysis
    public static bool RunAnalysisStage(Compilation compilation)
    {
        CreateAnalysisServices(compilation);
        PerformSemanticAnalysis(compilation);
        return !compilation.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    // Stage 3: Optimization
    public static void RunOptimizationStage(Compilation compilation)
    {
        if (compilation.Options.EnableConstantFolding && compilation.ProgramNode is not null)
        {
            AstOptimizer? optimizer = new();
            compilation.ProgramNode = optimizer.Optimize(compilation.ProgramNode);
        }
    }

    // Stage 4: Generation
    public static string? RunGenerationStage(Compilation compilation)
    {
        if (compilation.ProgramNode is null || compilation.TypeRepository is null || compilation.TypeResolver is null ||
            compilation.FunctionResolver is null || compilation.VTableManager is null ||
            compilation.MemoryLayoutManager is null || compilation.SemanticAnalyzer is null)
        {
            // This should not happen if previous stages succeeded
            return null;
        }

        CodeGenerator? generator = new(
            compilation.ProgramNode,
            compilation.TypeRepository,
            compilation.TypeResolver,
            compilation.FunctionResolver,
            compilation.VTableManager,
            compilation.MemoryLayoutManager,
            compilation.SemanticAnalyzer,
            compilation.Options);

        return generator.Generate();
    }

    private static void ParseIntoCompilationUnits(Compilation compilation)
    {
        List<CompilationUnitNode>? compilationUnits = new();
        List<ImportDirectiveNode>? allImports = new();

        foreach (string file in compilation.AllFiles)
        {
            string? code = File.ReadAllText(file);
            compilation.SourceFileCache[file] = code.Split('\n');

            List<Token>? tokens = Tokenizer.Tokenize(code);
            Parser? parser = new(tokens);
            CompilationUnitNode? unit = parser.Parse(file);

            compilation.Diagnostics.AddRange(parser.Diagnostics);

            List<ImportDirectiveNode>? importsInFile = parser.GetImports();
            allImports.AddRange(importsInFile);

            compilationUnits.Add(unit);
        }

        compilation.ProgramNode = new(allImports.DistinctBy(i => i.LibraryName).ToList(), compilationUnits);
    }

    private static void CreateAnalysisServices(Compilation compilation)
    {
        if (compilation.ProgramNode is null) return;

        var typeRepository = new TypeRepository(compilation.ProgramNode);
        var monomorphizer = new Monomorphizer(typeRepository);
        var typeResolver = new TypeResolver(typeRepository, monomorphizer);
        monomorphizer.SetResolver(typeResolver); // Break circular dependency
        var vtableManager = new VTableManager(typeRepository, typeResolver);
        var memoryLayoutManager = new MemoryLayoutManager(typeRepository, typeResolver, vtableManager);
        var functionResolver = new FunctionResolver(typeRepository, typeResolver, compilation.ProgramNode);
        var semanticAnalyzer = new SemanticAnalyzer(typeRepository, typeResolver, functionResolver, memoryLayoutManager);
        functionResolver.SetSemanticAnalyzer(semanticAnalyzer); // Break circular dependency

        // Store services in the compilation object
        compilation.TypeRepository = typeRepository;
        compilation.TypeResolver = typeResolver;
        compilation.VTableManager = vtableManager;
        compilation.MemoryLayoutManager = memoryLayoutManager;
        compilation.FunctionResolver = functionResolver;
        compilation.SemanticAnalyzer = semanticAnalyzer;
    }

    private static void PerformSemanticAnalysis(Compilation compilation)
    {
        if (compilation.ProgramNode is null || compilation.TypeRepository is null || compilation.TypeResolver is null ||
            compilation.FunctionResolver is null || compilation.MemoryLayoutManager is null || compilation.SemanticAnalyzer is null)
            return;

        var runner = new SemanticAnalyzerRunner(
            compilation.ProgramNode,
            compilation.TypeRepository,
            compilation.TypeResolver,
            compilation.FunctionResolver,
            compilation.MemoryLayoutManager,
            compilation.SemanticAnalyzer);

        runner.Analyze();
        compilation.Diagnostics.AddRange(runner.Diagnostics);
    }

    // Logic from former Preprocessor.cs
    private static List<string> DiscoverDependencies(string entryFilePath)
    {
        var finalOrder = new List<string>();
        var visited = new HashSet<string>();
        DiscoverRec(Path.GetFullPath(entryFilePath), visited, finalOrder);
        return finalOrder;
    }

    private static void DiscoverRec(string currentFilePath, HashSet<string> visited, List<string> finalOrder)
    {
        if (!File.Exists(currentFilePath) || visited.Contains(currentFilePath)) return;

        visited.Add(currentFilePath);
        var directory = Path.GetDirectoryName(currentFilePath) ?? "";
        var includes = new List<string>();

        foreach (var line in File.ReadLines(currentFilePath))
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("#include"))
            {
                var startIndex = trimmedLine.IndexOf('"');
                var endIndex = trimmedLine.LastIndexOf('"');
                if (startIndex != -1 && endIndex > startIndex)
                {
                    var includeFileName = trimmedLine.Substring(startIndex + 1, endIndex - startIndex - 1);
                    var fullIncludePath = Path.GetFullPath(Path.Combine(directory, includeFileName));
                    includes.Add(fullIncludePath);
                }
            }
        }

        foreach (var includePath in includes) DiscoverRec(includePath, visited, finalOrder);
        finalOrder.Add(currentFilePath);
    }
}