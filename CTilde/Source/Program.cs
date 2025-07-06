namespace CTilde;

public class Program
{
    public static void Main()
    {
        Compiler compiler = new();

        OptimizationOptions options = new()
        {
            EnableConstantFolding = true
        };

        compiler.Compile("Code/main.c", options);
    }
}