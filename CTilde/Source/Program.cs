using System;
using System.IO;

namespace CTilde;

public class Program
{
    public static void Main(string[] args)
    {
        string code = File.ReadAllText("Output/main.c");

        // 1. Tokenize
        var tokens = Tokenizer.Tokenize(code);

        // 2. Parse
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // 3. Generate
        var generator = new Generator(ast);
        string asmCode = generator.Generate();

        // 4. Output
        File.WriteAllText("Output/output.asm", asmCode);
        Console.WriteLine("Compilation successful. Assembly code written to output.asm");
    }
}