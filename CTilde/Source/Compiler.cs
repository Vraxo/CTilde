using System.Collections.Generic;
using System.IO;
using System.Linq;

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

        foreach (var file in allFiles)
        {
            var code = File.ReadAllText(file);
            var tokens = Tokenizer.Tokenize(code);
            var parser = new Parser(tokens);
            var unit = parser.Parse(file);

            // Collect all #import directives globally
            var importsInFile = parser.GetImports();
            allImports.AddRange(importsInFile);

            compilationUnits.Add(unit);
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