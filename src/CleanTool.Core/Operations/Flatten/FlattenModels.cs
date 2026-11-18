using CleanTool.Core.Geometry;
using CleanTool.Core.Model;

namespace CleanTool.Core.Operations.Flatten;

/// <summary>
/// Summary of an object that may need to be moved to Z = 0. Filled in by the gateway (AutoCAD layer).
/// </summary>
/// <param name="Handle">Object identity.</param>
/// <param name="TypeName">Type name shown in reports (Line, Circle, ...).</param>
/// <param name="MinZ">Lowest Z of the object (WCS).</param>
/// <param name="MaxZ">Highest Z of the object (WCS).</param>
/// <param name="IsOnLockedLayer">Objects on locked layers cannot be changed.</param>
public sealed record FlattenCandidate(
    EntityHandle Handle,
    string TypeName,
    double MinZ,
    double MaxZ,
    bool IsOnLockedLayer);

/// <summary>Settings of the flatten operation.</summary>
public sealed record FlattenOptions
{
    /// <summary>An object whose |Z| is below this value is considered flat already.</summary>
    public Tolerance ZTolerance { get; init; } = Tolerance.Default;
}

/// <summary>
/// Port through which the Flatten operation reads and changes the drawing. Interface segregation: only what this operation needs.
/// </summary>
public interface IFlattenGateway
{
    IReadOnlyList<FlattenCandidate> GetCandidates();

    /// <summary>Moves the object to Z = 0. On failure returns false and the reason (does not throw).</summary>
    bool TryFlatten(EntityHandle handle, out string? failureReason);
}
