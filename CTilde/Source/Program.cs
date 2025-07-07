using System;

namespace CTilde;

public class Program
{
    public static void Main(string[] args)
    {
        Compiler compiler = new();

        if (args.Length == 0)
        {
            // Default behavior when no arguments are provided.
            // This preserves the simple "F5 to run" development experience.
            Console.WriteLine("No input file specified. Compiling default 'Code/main.c'...");

            OptimizationOptions defaultOptions = new()
            {
                EnableConstantFolding = true,
                OutputType = OutputType.Console
            };

            compiler.Compile("Code/main.c", defaultOptions);
        }
        else
        {
            // Behavior when command-line arguments are provided.
            string entryFilePath = args[0];
            OptimizationOptions cliOptions = new(); // Starts with defaults

            // Simple argument parsing for flags.
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--gui":
                        cliOptions.OutputType = OutputType.Gui;
                        break;
                    case "--optimize":
                        cliOptions.EnableConstantFolding = true;
                        break;
                    default:
                        Console.WriteLine($"Warning: Unknown argument '{args[i]}' ignored.");
                        break;
                }
            }

            Console.WriteLine($"Compiling '{entryFilePath}'...");
            Console.WriteLine($"  Output Type: {cliOptions.OutputType}");
            Console.WriteLine($"  Constant Folding: {(cliOptions.EnableConstantFolding ? "Enabled" : "Disabled")}");

            compiler.Compile(entryFilePath, cliOptions);
        }
    }
}