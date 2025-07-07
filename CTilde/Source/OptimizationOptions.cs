namespace CTilde;

public class OptimizationOptions
{
    public bool EnableConstantFolding { get; set; } = false;
    public bool EnablePeepholeOptimization { get; set; } = false;
    public bool LogOptimizations { get; set; } = false;
    public string OptimizationLogPath { get; set; } = "optimizations.log";
    public OutputType OutputType { get; set; } = OutputType.Console;
}