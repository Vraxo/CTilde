using System;
using System.IO;

namespace CTilde;

public class Program
{
    public static void Main(string[] args)
    {
        // 0. Preprocess
        var preprocessor = new Preprocessor();
        string mergedCode = preprocessor.Process("Output/main.c");
        File.WriteAllText("Output/merged.c", mergedCode);
        Console.WriteLine("Preprocessing successful. Merged code written to Output/merged.c");

        // 1. Tokenize
        var tokens = Tokenizer.Tokenize(mergedCode);

        // 2. Parse
        var parser = new Parser(tokens);
        var ast = parser.Parse();

        // 3. Generate
        var generator = new CodeGenerator(ast);
        string asmCode = generator.Generate();

        // 4. Output
        File.WriteAllText("Output/output.asm", asmCode);
        Console.WriteLine("Compilation successful. Assembly code written to output.asm");
    }
}