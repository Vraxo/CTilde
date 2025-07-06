namespace CTilde;

/// <summary>
/// Provides configuration options to control the compilation process,
/// particularly which optimizations to enable.
/// </summary>
public class OptimizationOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to enable constant folding.
    /// This optimization evaluates constant expressions at compile-time.
    /// Default is false.
    /// </summary>
    public bool EnableConstantFolding { get; set; } = false;
}