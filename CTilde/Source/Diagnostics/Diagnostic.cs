namespace CTilde.Diagnostics;

public record Diagnostic(string FilePath, string Message, int Line, int Column);