using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTilde.Diagnostics;

namespace CTilde;

public class Compiler
{
    public void Compile(string entryFilePath)
    {
        // 1. Discover all source files from #includes
        var preprocessor = new Preprocessor();
        var allFiles = preprocessor.DiscoverDependencies(entryFilePath);

        // 2. Parse each file into a CompilationUnit
        var compilationUnits = new List<CompilationUnitNode>();
        var allImports = new List<ImportDirectiveNode>();
        var allDiagnostics = new List<Diagnostic>();
        var sourceFileCache = new Dictionary<string, string[]>();

        foreach (var file in allFiles)
        {
            var code = File.ReadAllText(file);
            sourceFileCache[file] = code.Split('\n');

            var tokens = Tokenizer.Tokenize(code);
            var parser = new Parser(tokens);
            var unit = parser.Parse(file);

            allDiagnostics.AddRange(parser.Diagnostics);

            var importsInFile = parser.GetImports();
            allImports.AddRange(importsInFile);

            compilationUnits.Add(unit);
        }

        // --- Stop if there are parsing errors ---
        if (allDiagnostics.Any())
        {
            var printer = new DiagnosticPrinter(allDiagnostics, sourceFileCache);
            printer.Print();
            Console.WriteLine($"\nCompilation failed with {allDiagnostics.Count} parsing error(s).");
            return;
        }

        var programNode = new ProgramNode(allImports.DistinctBy(i => i.LibraryName).ToList(), compilationUnits);

        // 3. Create analysis services ONCE
        var typeRepository = new TypeRepository(programNode);
        var typeResolver = new TypeResolver(typeRepository);
        var vtableManager = new VTableManager(typeRepository, typeResolver);
        var memoryLayoutManager = new MemoryLayoutManager(typeRepository, typeResolver, vtableManager);
        var functionResolver = new FunctionResolver(typeRepository, typeResolver, programNode);
        var semanticAnalyzer = new SemanticAnalyzer(typeRepository, typeResolver, functionResolver, memoryLayoutManager);

        // 4. Perform Semantic Analysis
        var runner = new SemanticAnalyzerRunner(programNode, typeRepository, typeResolver, functionResolver, memoryLayoutManager, semanticAnalyzer);
        runner.Analyze();

        allDiagnostics.AddRange(runner.Diagnostics);

        // --- Stop if there are semantic errors ---
        if (allDiagnostics.Any())
        {
            var printer = new DiagnosticPrinter(allDiagnostics, sourceFileCache);
            printer.Print();
            Console.WriteLine($"\nCompilation failed with {allDiagnostics.Count} semantic error(s).");
            return;
        }

        // 5. Generate Code
        var generator = new CodeGenerator(programNode, typeRepository, typeResolver, functionResolver, vtableManager, memoryLayoutManager, semanticAnalyzer);
        string asmCode = generator.Generate();

        // 6. Output
        File.WriteAllText("Output/output.asm", asmCode);
        Console.WriteLine("Compilation successful. Assembly code written to output.asm");
    }
}