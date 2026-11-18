namespace CleanTool.Core.Operations.Layers;

/// <summary>Outcome of evaluating one layer.</summary>
public enum LayerDecision
{
    Delete,
    KeepInUse,
    KeepProtected,
    KeepExcluded
}

/// <summary>
/// Pure rule class that decides whether a layer may be deleted.
/// </summary>
public sealed class LayerPurgePolicy
{
    /// <summary>System layers that AutoCAD never allows to be deleted.</summary>
    private static readonly string[] BuiltInLayers = { "0", "Defpoints" };

    private readonly LayerPurgeOptions _options;

    /// <summary>Creates the policy with the given exclusions.</summary>
    public LayerPurgePolicy(LayerPurgeOptions? options = null)
    {
        _options = options ?? new LayerPurgeOptions();
    }

    /// <summary>Decides whether the layer may be deleted and, if not, why.</summary>
    public LayerDecision Evaluate(LayerRecord layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if (IsProtected(layer)) return LayerDecision.KeepProtected;
        if (layer.IsInUse) return LayerDecision.KeepInUse;
        if (IsExcluded(layer.Name)) return LayerDecision.KeepExcluded;

        return LayerDecision.Delete;
    }

    private static bool IsProtected(LayerRecord layer) =>
        layer.IsCurrent
        || layer.IsXrefDependent
        || BuiltInLayers.Contains(layer.Name, StringComparer.OrdinalIgnoreCase);

    private bool IsExcluded(string name) =>
        _options.ExcludedPatterns.Any(p => WildcardMatcher.IsMatch(name, p));
}
