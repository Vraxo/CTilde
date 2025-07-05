using System;

namespace CTilde;

public class Program
{
    public static void Main(string[] args)
    {
        var compiler = new Compiler();
        compiler.Compile("Code/main.c");
    }
}