using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CTilde.Diagnostics;
using System.Diagnostics;
using System.Text;
using CTilde.Analysis;

namespace CTilde;

public class Compiler
{
    /// <summary>
    /// Compiles the entry file with default options (no optimizations).
    /// </summary>
    public void Compile(string entryFilePath)
    {
        Compile(entryFilePath, new OptimizationOptions());
    }

    /// <summary>
    /// Compiles the entry file with the specified compilation options.
    /// </summary>
    public void Compile(string entryFilePath, OptimizationOptions options)
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

        var programNode = new ProgramNode(allImports.DistinctBy(i => i.LibraryName).ToList(), compilationUnits);

        // 3. Create analysis services ONCE
        var typeRepository = new TypeRepository(programNode);
        var monomorphizer = new Monomorphizer(typeRepository);
        var typeResolver = new TypeResolver(typeRepository, monomorphizer);
        monomorphizer.SetResolver(typeResolver); // Break circular dependency
        var vtableManager = new VTableManager(typeRepository, typeResolver);
        var memoryLayoutManager = new MemoryLayoutManager(typeRepository, typeResolver, vtableManager);
        var functionResolver = new FunctionResolver(typeRepository, typeResolver, programNode);
        var semanticAnalyzer = new SemanticAnalyzer(typeRepository, typeResolver, functionResolver, memoryLayoutManager);
        functionResolver.SetSemanticAnalyzer(semanticAnalyzer); // Break circular dependency

        // 4. Perform Semantic Analysis
        var runner = new SemanticAnalyzerRunner(programNode, typeRepository, typeResolver, functionResolver, memoryLayoutManager, semanticAnalyzer);
        runner.Analyze();

        allDiagnostics.AddRange(runner.Diagnostics);

        // 4.5 Perform Optimizations (if enabled)
        if (options.EnableConstantFolding)
        {
            var optimizer = new AstOptimizer();
            programNode = optimizer.Optimize(programNode);
        }

        // --- Print all diagnostics, but only fail on errors ---
        if (allDiagnostics.Any())
        {
            var printer = new DiagnosticPrinter(allDiagnostics, sourceFileCache);
            printer.Print();

            var errorCount = allDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            if (errorCount > 0)
            {
                Console.WriteLine($"\nCompilation failed with {errorCount} error(s).");
                return;
            }
        }

        // 5. Generate Code
        var generator = new CodeGenerator(programNode, typeRepository, typeResolver, functionResolver, vtableManager, memoryLayoutManager, semanticAnalyzer);
        string asmCode = generator.Generate();

        // 6. Output
        string outputAsmPath = "Code/Output/output.asm";
        File.WriteAllText(outputAsmPath, asmCode);
        Console.WriteLine($"Compilation successful. Assembly code written to {outputAsmPath}");

        // 7. Execute FASM directly
        ExecuteFasm(outputAsmPath);
    }

    private void ExecuteFasm(string relativeAsmPath)
    {
        // Define paths for FASM. A more advanced implementation could find this in PATH or via configuration.
        string fasmPath = @"D:\Program Files\FASM\fasm.exe";
        string fasmIncludePath = @"D:\Program Files\FASM\INCLUDE";

        if (!File.Exists(fasmPath))
        {
            Console.Error.WriteLine($"\nASSEMBLER ERROR: FASM executable not found at the expected path: '{fasmPath}'.");
            Console.Error.WriteLine("Please install FASM (flat assembler) to 'D:\\Program Files\\FASM\\' or update the path in Compiler.cs.");
            return;
        }

        if (!Directory.Exists(fasmIncludePath))
        {
            Console.Error.WriteLine($"\nASSEMBLER ERROR: FASM include directory not found at the expected path: '{fasmIncludePath}'.");
            return;
        }

        try
        {
            Console.WriteLine($"\nExecuting FASM assembler...");

            string fullAsmPath = Path.GetFullPath(relativeAsmPath);
            string workingDirectory = Path.GetDirectoryName(fullAsmPath) ?? ".";

            // The fasm command needs the output file path relative to its working directory.
            // Since we set the working directory to the same folder as the asm file, we only need the filename.
            string fasmArgument = Path.GetFileName(fullAsmPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = fasmPath,
                Arguments = fasmArgument,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };

            // Set the INCLUDE environment variable specifically for the FASM process
            startInfo.EnvironmentVariables["INCLUDE"] = fasmIncludePath;

            using (var process = new Process { StartInfo = startInfo })
            {
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, args) => { if (args.Data != null) outputBuilder.AppendLine(args.Data); };
                process.ErrorDataReceived += (sender, args) => { if (args.Data != null) errorBuilder.AppendLine(args.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                Console.Write(outputBuilder.ToString());
                Console.Error.Write(errorBuilder.ToString());

                if (process.ExitCode == 0)
                {
                    Console.WriteLine("FASM execution successful.");
                }
                else
                {
                    Console.Error.WriteLine($"FASM execution failed with exit code {process.ExitCode}.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error executing FASM: {ex.Message}");
        }
    }
}