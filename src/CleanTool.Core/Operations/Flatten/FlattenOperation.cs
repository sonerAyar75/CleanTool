using CleanTool.Core.Abstractions;
using CleanTool.Core.Pipeline;

namespace CleanTool.Core.Operations.Flatten;

/// <summary>
/// Moves all objects to the Z = 0 plane.
/// The decisions (which object is flat, which is skipped) are made here; HOW an object is flattened is the gateway's job.
/// </summary>
public sealed class FlattenOperation : ICleanOperation
{
    /// <summary>Stable operation id used by commands and settings.</summary>
    public const string OperationId = "flatten";

    private readonly IFlattenGateway _gateway;
    private readonly FlattenOptions _options;

    /// <summary>Creates the operation.</summary>
    /// <param name="gateway">Access to the drawing.</param>
    /// <param name="options">Settings; defaults are used when null.</param>
    public FlattenOperation(IFlattenGateway gateway, FlattenOptions? options = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _options = options ?? new FlattenOptions();
    }

    /// <inheritdoc/>
    public string Id => OperationId;

    /// <inheritdoc/>
    public string DisplayName => "Flatten (Z=0)";

    /// <inheritdoc/>
    public CleanStage Stage => CleanStage.Flatten;

    /// <inheritdoc/>
    public OperationResult Execute(CleanContext context)
    {
        var result = new OperationResultBuilder(Id, DisplayName);

        foreach (var candidate in _gateway.GetCandidates())
        {
            result.AddExamined();

            if (IsAlreadyFlat(candidate)) continue;

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

            if (_gateway.TryFlatten(candidate.Handle, out var reason))
                result.AddChanged();
            else
                result.AddFailed($"{candidate.TypeName} <{candidate.Handle}>: {reason ?? "unknown error"}");
        }

        return result.Build();
    }

    internal bool IsAlreadyFlat(FlattenCandidate candidate) =>
        _options.ZTolerance.IsZero(candidate.MinZ) && _options.ZTolerance.IsZero(candidate.MaxZ);
}
