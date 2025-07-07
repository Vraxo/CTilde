using System;
using System.IO;
using System.Linq;
using CTilde.Diagnostics;

namespace CTilde;

public class Compiler
{
    public void Compile(string entryFilePath)
    {
        Compile(entryFilePath, new());
    }

    public void Compile(string entryFilePath, OptimizationOptions options)
    {
        // 1. Initialize state for this compilation run
        var compilation = new Compilation(entryFilePath, options);

        // 2. Run parsing stage
        if (!CompilationPipeline.RunParsingStage(compilation))
        {
            PrintDiagnostics(compilation);
            return;
        }

        // 3. Run analysis stage
        if (!CompilationPipeline.RunAnalysisStage(compilation))
        {
            PrintDiagnostics(compilation);
            return;
        }

        // 4. Run optional optimization stage
        CompilationPipeline.RunOptimizationStage(compilation);

        // 5. Print all diagnostics and check for fatal errors before proceeding
        PrintDiagnostics(compilation);
        if (compilation.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return;
        }

        // 6. Generate Code
        string? asmCode = CompilationPipeline.RunGenerationStage(compilation);
        if (asmCode is null)
        {
            // This case should ideally be caught by earlier error checks.
            Console.Error.WriteLine("\nInternal Compiler Error: Code generation failed unexpectedly.");
            return;
        }

        // 7. Output to file
        string? outputAsmPath = OutputAssembly(asmCode, compilation.EntryFilePath);

        // 8. Execute FASM to assemble the code
        var assembler = new Assembler();
        assembler.Assemble(outputAsmPath);
    }

    private void PrintDiagnostics(Compilation compilation)
    {
        if (compilation.Diagnostics.Count == 0) return;

        var printer = new DiagnosticPrinter(compilation.Diagnostics, compilation.SourceFileCache);
        printer.Print();

        int errorCount = compilation.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        if (errorCount > 0)
        {
            Console.WriteLine($"\nCompilation failed with {errorCount} error(s).");
        }
    }

    private static string OutputAssembly(string asmCode, string entryFilePath)
    {
        // Place the output relative to the source file, which is more intuitive.
        string sourceDirectory = Path.GetDirectoryName(entryFilePath) ?? ".";
        string outputDirectory = Path.Combine(sourceDirectory, "output");

        // Ensure the output directory exists before writing to it.
        // This is the direct fix for the DirectoryNotFoundException.
        Directory.CreateDirectory(outputDirectory);

        string outputAsmPath = Path.Combine(outputDirectory, "output.asm");

        File.WriteAllText(outputAsmPath, asmCode);
        Console.WriteLine($"Compilation successful. Assembly code written to {Path.GetFullPath(outputAsmPath)}");
        return outputAsmPath;
    }
}