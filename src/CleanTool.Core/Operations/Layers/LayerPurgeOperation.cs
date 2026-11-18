using CleanTool.Core.Abstractions;
using CleanTool.Core.Pipeline;

namespace CleanTool.Core.Operations.Layers;

/// <summary>
/// Deletes unused layers. Runs in the <see cref="CleanStage.Purge"/> stage, after duplicate removal,
/// because erased objects may have emptied some layers.
/// </summary>
public sealed class LayerPurgeOperation : ICleanOperation
{
    /// <summary>Stable operation id used by commands and settings.</summary>
    public const string OperationId = "purge-layers";

    private readonly ILayerGateway _gateway;
    private readonly LayerPurgePolicy _policy;

    /// <summary>Creates the operation.</summary>
    /// <param name="gateway">Access to the layer table.</param>
    /// <param name="options">Settings; defaults are used when null.</param>
    public LayerPurgeOperation(ILayerGateway gateway, LayerPurgeOptions? options = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _policy = new LayerPurgePolicy(options);
    }

    /// <inheritdoc/>
    public string Id => OperationId;

    /// <inheritdoc/>
    public string DisplayName => "Unused layers";

    /// <inheritdoc/>
    public CleanStage Stage => CleanStage.Purge;

    /// <inheritdoc/>
    public OperationResult Execute(CleanContext context)
    {
        var result = new OperationResultBuilder(Id, DisplayName);

        foreach (var layer in _gateway.GetLayers())
        {
            result.AddExamined();

            switch (_policy.Evaluate(layer))
            {
                case LayerDecision.KeepExcluded:
                    result.AddSkipped($"Layer '{layer.Name}' is unused but excluded by pattern.");
                    break;

                case LayerDecision.Delete when context.DryRun:
                    result.AddChanged();
                    result.AddMessage($"Layer '{layer.Name}' would be deleted.");
                    break;

                case LayerDecision.Delete:
                    if (_gateway.TryDelete(layer.Name, out var reason))
                    {
                        result.AddChanged();
                        result.AddMessage($"Layer '{layer.Name}' deleted.");
                    }
                    else
                    {
                        result.AddFailed($"Layer '{layer.Name}': {reason ?? "unknown error"}");
                    }
                    break;

                // KeepInUse / KeepProtected: the normal case, no need to add noise to the report.
            }
        }

        return result.Build();
    }
}
