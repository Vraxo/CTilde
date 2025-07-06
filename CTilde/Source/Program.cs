namespace CTilde;

public class Program
{
    public static void Main(string[] args)
    {
        var compiler = new Compiler();
        var options = new OptimizationOptions
        {
            EnableConstantFolding = false
        };
        compiler.Compile("Code/main.c", options);
    }
}