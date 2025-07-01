namespace CTilde;

public class Program
{
    public static void Main(string[] args)
    {
        string code = File.ReadAllText("main.c");

        Tokenizer.Tokenize(code);
    }
}