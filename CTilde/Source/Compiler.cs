using System.Diagnostics;
using System.Text;
using CTilde.Analysis;
using CTilde.Diagnostics;

namespace CTilde;

public class Compiler
{
    private string? _entryFilePath;
    private OptimizationOptions _options;
    private List<string>? _allFiles;
    private readonly List<Diagnostic> _allDiagnostics = [];
    private readonly Dictionary<string, string[]> _sourceFileCache = [];
    private ProgramNode? _programNode;

    private TypeRepository? _typeRepository;
    private TypeResolver? _typeResolver;
    private VTableManager? _vtableManager;
    private MemoryLayoutManager? _memoryLayoutManager;
    private FunctionResolver? _functionResolver;
    private SemanticAnalyzer? _semanticAnalyzer;

    public void Compile(string entryFilePath)
    {
        Compile(entryFilePath, new());
    }

    public void Compile(string entryFilePath, OptimizationOptions options)
    {
        // 1. Initialize state for this compilation run
        _entryFilePath = entryFilePath;
        _options = options;
        _allDiagnostics.Clear();
        _sourceFileCache.Clear();
        // Other fields will be overwritten, no need to clear/null them.

        // 2. Discover all source files
        _allFiles = GetAllFiles(_entryFilePath);

        // 3. Parse each file into a CompilationUnit
        ParseIntoCompilationUnits();

        // 4. Create analysis services
        CreateAnalysisServices();

        // 5. Perform Semantic Analysis
        PerformSemanticAnalysis();

        // 6. Perform Optimizations (if enabled)
        Optimize();

        // 7. Print all diagnostics and check for fatal errors before proceeding
        PrintDiagnostics();
        if (_allDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return;
        }

        // 8. Generate Code
        string? asmCode = GenerateCode();

        // 9. Output to file
        string? outputAsmPath = OutputAssembly(asmCode);

        // 10. Execute FASM to assemble the code
        Assemble(outputAsmPath);
    }

    private void ParseIntoCompilationUnits()
    {
        List<CompilationUnitNode>? compilationUnits = [];
        List<ImportDirectiveNode>? allImports = [];

        foreach (string file in _allFiles)
        {
            string? code = File.ReadAllText(file);
            _sourceFileCache[file] = code.Split('\n');

            List<Token>? tokens = Tokenizer.Tokenize(code);
            Parser? parser = new(tokens);
            CompilationUnitNode? unit = parser.Parse(file);

            _allDiagnostics.AddRange(parser.Diagnostics);

            List<ImportDirectiveNode>? importsInFile = parser.GetImports();
            allImports.AddRange(importsInFile);

            compilationUnits.Add(unit);
        }

        _programNode = new(allImports.DistinctBy(i => i.LibraryName).ToList(), compilationUnits);
    }

    private void CreateAnalysisServices()
    {
        _typeRepository = new(_programNode);
        Monomorphizer? monomorphizer = new(_typeRepository);
        _typeResolver = new(_typeRepository, monomorphizer);
        monomorphizer.SetResolver(_typeResolver); // Break circular dependency
        _vtableManager = new(_typeRepository, _typeResolver);
        _memoryLayoutManager = new(_typeRepository, _typeResolver, _vtableManager);
        _functionResolver = new(_typeRepository, _typeResolver, _programNode);
        _semanticAnalyzer = new(_typeRepository, _typeResolver, _functionResolver, _memoryLayoutManager);
        _functionResolver.SetSemanticAnalyzer(_semanticAnalyzer); // Break circular dependency
    }

    private void PerformSemanticAnalysis()
    {
        var runner = new SemanticAnalyzerRunner(_programNode, _typeRepository, _typeResolver, _functionResolver, _memoryLayoutManager, _semanticAnalyzer);
        runner.Analyze();
        _allDiagnostics.AddRange(runner.Diagnostics);
    }

    private void Optimize()
    {
        if (_options.EnableConstantFolding)
        {
            AstOptimizer? optimizer = new();
            _programNode = optimizer.Optimize(_programNode);
        }
    }

    private void PrintDiagnostics()
    {
        if (_allDiagnostics.Count == 0)
        {
            return;
        }

        DiagnosticPrinter? printer = new(_allDiagnostics, _sourceFileCache);
        printer.Print();

        int? errorCount = _allDiagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);

        if (errorCount > 0)
        {
            Console.WriteLine($"\nCompilation failed with {errorCount} error(s).");
        }
    }

    private string GenerateCode()
    {
        CodeGenerator? generator = new(_programNode, _typeRepository, _typeResolver, _functionResolver, _vtableManager, _memoryLayoutManager, _semanticAnalyzer);
        string? asmCode = generator.Generate();
        return asmCode;
    }

    private static string OutputAssembly(string asmCode)
    {
        string? outputAsmPath = "Code/Output/output.asm";
        File.WriteAllText(outputAsmPath, asmCode);
        Console.WriteLine($"Compilation successful. Assembly code written to {outputAsmPath}");
        return outputAsmPath;
    }

    private static void Assemble(string relativeAsmPath)
    {
        // Define paths for FASM. A more advanced implementation could find this in PATH or via configuration.
        string? fasmPath = @"D:\Program Files\FASM\fasm.exe";
        string? fasmIncludePath = @"D:\Program Files\FASM\INCLUDE";

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

            string? fullAsmPath = Path.GetFullPath(relativeAsmPath);
            string? workingDirectory = Path.GetDirectoryName(fullAsmPath) ?? ".";

            // The fasm command needs the output file path relative to its working directory.
            // Since we set the working directory to the same folder as the asm file, we only need the filename.
            string? fasmArgument = Path.GetFileName(fullAsmPath);

            ProcessStartInfo? startInfo = StartAssemblerProcess(fasmPath, workingDirectory, fasmArgument);

            // Set the INCLUDE environment variable specifically for the FASM process
            startInfo.EnvironmentVariables["INCLUDE"] = fasmIncludePath;

            using Process? process = new() { StartInfo = startInfo };
            StringBuilder? outputBuilder = new();
            StringBuilder? errorBuilder = new();

            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data is null)
                {
                    return;
                }

                outputBuilder.AppendLine(args.Data);
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data is not null)
                {
                    errorBuilder.AppendLine(args.Data);
                }
            };

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
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error executing FASM: {ex.Message}");
        }
    }

    private static ProcessStartInfo StartAssemblerProcess(string fasmPath, string workingDirectory, string fasmArgument)
    {
        return new()
        {
            FileName = fasmPath,
            Arguments = fasmArgument,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
    }

    private static List<string> GetAllFiles(string entryFilePath)
    {
        return new Preprocessor().DiscoverDependencies(entryFilePath);
    }
}