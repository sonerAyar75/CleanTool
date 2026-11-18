namespace CleanTool.Core.Abstractions;

/// <summary>
/// Port through which operations report messages to the user.
/// The AutoCAD layer writes them to the command line; tests collect them in a list.
/// </summary>
public interface ICleanLogger
{
    void Info(string message);
    void Warn(string message);
}

/// <summary>Logger that discards every message (Null Object pattern).</summary>
public sealed class NullCleanLogger : ICleanLogger
{
    /// <summary>Shared instance; the logger has no state.</summary>
    public static NullCleanLogger Instance { get; } = new();

    private NullCleanLogger() { }

    /// <inheritdoc/>
    public void Info(string message) { }
    /// <inheritdoc/>
    public void Warn(string message) { }
}
