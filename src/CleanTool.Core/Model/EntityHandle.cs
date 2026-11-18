namespace CleanTool.Core.Model;

/// <summary>
/// Platform-independent identity of a drawing object.
/// Maps one-to-one to AutoCAD's Handle.Value (long); unlike ObjectId it is stable across sessions.
/// </summary>
public readonly record struct EntityHandle(long Value)
{
    /// <summary>Hexadecimal representation, as AutoCAD shows handles.</summary>
    public override string ToString() => Value.ToString("X");
}
