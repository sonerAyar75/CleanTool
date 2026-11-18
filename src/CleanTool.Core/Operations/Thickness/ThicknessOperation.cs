using CleanTool.Core.Abstractions;
using CleanTool.Core.Geometry;
using CleanTool.Core.Model;
using CleanTool.Core.Pipeline;

namespace CleanTool.Core.Operations.Thickness;

/// <summary>Summary of an object that has a thickness property.</summary>
public sealed record ThicknessCandidate(
    EntityHandle Handle,
    string TypeName,
    double Thickness,
    bool IsOnLockedLayer);

/// <summary>Settings of the thickness reset.</summary>
public sealed record ThicknessOptions
{
    /// <summary>A thickness whose absolute value is below this is considered zero.</summary>
    public Tolerance Tolerance { get; init; } = Tolerance.Default;
}

/// <summary>Port through which the thickness operation reads and changes the drawing.</summary>
public interface IThicknessGateway
{
    /// <summary>Objects that have a thickness property (Line, Circle, Arc, Polyline, Text, ...).</summary>
    IReadOnlyList<ThicknessCandidate> GetCandidates();

    bool TryResetThickness(EntityHandle handle, out string? failureReason);
}

/// <summary>
/// Resets the thickness (extrusion along Z) of objects.
/// Runs in the <see cref="CleanStage.Normalize"/> stage, BEFORE Z = 0: thickness enlarges the Z range, so without
/// resetting it first even flat objects would look 3D to the flatten step.
/// </summary>
public sealed class ThicknessOperation : ICleanOperation
{
    /// <summary>Stable operation id used by commands and settings.</summary>
    public const string OperationId = "thickness";

    private readonly IThicknessGateway _gateway;
    private readonly ThicknessOptions _options;

    /// <summary>Creates the operation.</summary>
    /// <param name="gateway">Access to the drawing.</param>
    /// <param name="options">Settings; defaults are used when null.</param>
    public ThicknessOperation(IThicknessGateway gateway, ThicknessOptions? options = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _options = options ?? new ThicknessOptions();
    }

    /// <inheritdoc/>
    public string Id => OperationId;

    /// <inheritdoc/>
    public string DisplayName => "Thickness = 0";

    /// <inheritdoc/>
    public CleanStage Stage => CleanStage.Normalize;

    /// <inheritdoc/>
    public OperationResult Execute(CleanContext context)
    {
        var result = new OperationResultBuilder(Id, DisplayName);

        foreach (var candidate in _gateway.GetCandidates())
        {
            result.AddExamined();

            if (_options.Tolerance.IsZero(candidate.Thickness)) continue;

            if (candidate.IsOnLockedLayer)
            {
                result.AddSkipped($"{candidate.TypeName} <{candidate.Handle}> is on a locked layer.");
                continue;
            }

            if (context.DryRun)
            {
                result.AddChanged();
                continue;
            }

            if (_gateway.TryResetThickness(candidate.Handle, out var reason))
                result.AddChanged();
            else
                result.AddFailed($"{candidate.TypeName} <{candidate.Handle}>: {reason ?? "unknown error"}");
        }

        return result.Build();
    }
}
