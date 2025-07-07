using System;
using System.IO;

namespace CTilde.Diagnostics;

public class OptimizationLogger : IDisposable
{
    private readonly StreamWriter? _writer;

    public OptimizationLogger(string logPath)
    {
        // Clear the log file at the start of compilation
        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }
        _writer = new StreamWriter(logPath, append: true);
    }

    public void Log(string pass, string before, string after, string? context)
    {
        if (_writer is null) return;

        _writer.WriteLine($"[{pass}]");
        _writer.WriteLine($"  Context: {context ?? "N/A"}");
        _writer.WriteLine($"  Before:  {before}");
        _writer.WriteLine($"  After:   {after}");
        _writer.WriteLine();
    }

    public void Dispose()
    {
        _writer?.Flush();
        _writer?.Dispose();
        GC.SuppressFinalize(this);
    }
}