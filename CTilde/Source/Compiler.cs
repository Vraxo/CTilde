using System.Diagnostics;
using CTilde.Diagnostics;

namespace CTilde;

public class Compiler
{
    public void Compile(string entryFilePath)
    {
        Preprocessor preprocessor = new();
        List<string> allFiles = preprocessor.DiscoverDependencies(entryFilePath);

        List<CompilationUnitNode> compilationUnits = [];
        List<ImportDirectiveNode> allImports = [];
        List<Diagnostic> allDiagnostics = [];
        Dictionary<string, string[]> sourceFileCache = [];

        foreach (string file in allFiles)
        {
            string code = File.ReadAllText(file);
            sourceFileCache[file] = code.Split('\n');

            List<Token> tokens = Tokenizer.Tokenize(code);
            Parser parser = new(tokens);
            CompilationUnitNode unit = parser.Parse(file);

            allDiagnostics.AddRange(parser.Diagnostics);

            List<ImportDirectiveNode> importsInFile = parser.GetImports();
            allImports.AddRange(importsInFile);

            compilationUnits.Add(unit);
        }

        ProgramNode programNode = new(allImports.DistinctBy(i => i.LibraryName).ToList(), compilationUnits);

        TypeRepository typeRepository = new(programNode);
        Monomorphizer monomorphizer = new(typeRepository);
        TypeResolver typeResolver = new(typeRepository, monomorphizer);
        monomorphizer.SetResolver(typeResolver); // Break circular dependency
        VTableManager vtableManager = new(typeRepository, typeResolver);
        MemoryLayoutManager memoryLayoutManager = new(typeRepository, typeResolver, vtableManager);
        FunctionResolver functionResolver = new(typeRepository, typeResolver, programNode);
        SemanticAnalyzer semanticAnalyzer = new(typeRepository, typeResolver, functionResolver, memoryLayoutManager);
        functionResolver.SetSemanticAnalyzer(semanticAnalyzer); // Break circular dependency

        SemanticAnalyzerRunner runner = new(programNode, typeRepository, typeResolver, functionResolver, memoryLayoutManager, semanticAnalyzer);
        runner.Analyze();

        allDiagnostics.AddRange(runner.Diagnostics);

        // --- Print all diagnostics, but only fail on errors ---
        if (allDiagnostics.Count != 0)
        {
            DiagnosticPrinter printer = new(allDiagnostics, sourceFileCache);
            printer.Print();

            int errorCount = allDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            
            if (errorCount > 0)
            {
                Console.WriteLine($"\nCompilation failed with {errorCount} error(s).");
                return;
            }
        }

        // 5. Generate Code
        CodeGenerator generator = new(programNode, typeRepository, typeResolver, functionResolver, vtableManager, memoryLayoutManager, semanticAnalyzer);
        string asmCode = generator.Generate();

        // 6. Output
        File.WriteAllText("Code/Output/output.asm", asmCode);
        Console.WriteLine("Compilation successful. Assembly code written to output.asm");

        // 7. Execute compile.bat
        string compileBatchPath = "Code/compile.bat";
        
        if (File.Exists(compileBatchPath))
        {
            try
            {
                Console.WriteLine($"Executing '{compileBatchPath}'...");
                
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = compileBatchPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(compileBatchPath) // Set working directory to the script's directory
                };

                using (Process process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    Console.WriteLine(process.StandardOutput.ReadToEnd());
                    Console.Error.WriteLine(process.StandardError.ReadToEnd());
                }

                Console.WriteLine($"Finished executing '{compileBatchPath}'.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error executing '{compileBatchPath}': {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"Warning: '{compileBatchPath}' not found. Skipping batch execution.");
        }
    }
}