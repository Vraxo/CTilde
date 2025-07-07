namespace CTilde;

public class Program
{
    public static void Main()
    {
        Compiler compiler = new();

        OptimizationOptions options = new()
        {
            EnableConstantFolding = true,
            OutputType = OutputType.Console
        };

        compiler.Compile("Code/main.c", options);
    }
}