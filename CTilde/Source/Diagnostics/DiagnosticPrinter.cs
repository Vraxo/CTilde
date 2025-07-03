using System;
using System.Collections.Generic;
using System.Linq;

namespace CTilde.Diagnostics;

public class DiagnosticPrinter
{
    private readonly IReadOnlyList<Diagnostic> _diagnostics;
    private readonly IReadOnlyDictionary<string, string[]> _sourceFiles;

    public DiagnosticPrinter(IReadOnlyList<Diagnostic> diagnostics, IReadOnlyDictionary<string, string[]> sourceFiles)
    {
        _diagnostics = diagnostics;
        _sourceFiles = sourceFiles;
    }

    public void Print()
    {
        foreach (var diagnostic in _diagnostics.OrderBy(d => d.FilePath).ThenBy(d => d.Line).ThenBy(d => d.Column))
        {
            if (!_sourceFiles.TryGetValue(diagnostic.FilePath, out var lines))
            {
                // Fallback for cases where source isn't available
                Console.Error.WriteLine($"Error: {diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): {diagnostic.Message}");
                continue;
            }

            Console.Error.WriteLine(); // Blank line for separation
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.Write($"Error: ");
            Console.ResetColor();
            Console.Error.WriteLine($"{diagnostic.Message}");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Error.WriteLine($"  --> {diagnostic.FilePath}:{diagnostic.Line}:{diagnostic.Column}");
            Console.Error.WriteLine($"   |");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Error.Write($"{diagnostic.Line,2} | ");
            Console.ResetColor();

            string line = lines[diagnostic.Line - 1];
            Console.Error.WriteLine(line);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Error.Write($"   | ");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(new string(' ', diagnostic.Column - 1) + "^");
            Console.ResetColor();
        }
    }
}