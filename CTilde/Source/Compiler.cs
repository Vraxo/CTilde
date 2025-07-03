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

        foreach (var file in allFiles)
        {
            var code = File.ReadAllText(file);
            var tokens = Tokenizer.Tokenize(code);
            var parser = new Parser(tokens);
            var unit = parser.Parse(file);

            allDiagnostics.AddRange(parser.Diagnostics);

            // Collect all #import directives globally, even if there are errors,
            // as they might be needed for other analysis.
            var importsInFile = parser.GetImports();
            allImports.AddRange(importsInFile);

            compilationUnits.Add(unit);
        }

        if (allDiagnostics.Any())
        {
            foreach (var diagnostic in allDiagnostics.OrderBy(d => d.FilePath).ThenBy(d => d.Line).ThenBy(d => d.Column))
            {
                Console.Error.WriteLine($"Error: {diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): {diagnostic.Message}");
            }
            Console.WriteLine($"\nCompilation failed with {allDiagnostics.Count} error(s).");
            return;
        }

        var programNode = new ProgramNode(allImports.DistinctBy(i => i.LibraryName).ToList(), compilationUnits);

        // 3. Generate Code
        var generator = new CodeGenerator(programNode);
        string asmCode = generator.Generate();

        // 4. Output
        File.WriteAllText("Output/output.asm", asmCode);
        Console.WriteLine("Compilation successful. Assembly code written to output.asm");
    }
}