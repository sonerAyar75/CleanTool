namespace CleanTool.Core.Operations.Layers;

/// <summary>Summary of a layer.</summary>
/// <param name="Name">Layer name.</param>
/// <param name="IsInUse">Is the layer used by any object (including objects in block definitions)?</param>
/// <param name="IsCurrent">The current layer cannot be deleted.</param>
/// <param name="IsXrefDependent">Xref-dependent layers ("XREF|LAYER") cannot be deleted.</param>
public sealed record LayerRecord(
    string Name,
    bool IsInUse,
    bool IsCurrent,
    bool IsXrefDependent);

/// <summary>Settings of the unused layer purge.</summary>
public sealed record LayerPurgeOptions
{
    /// <summary>
    /// Wildcard patterns (* and ?) of layer names that are kept even when unused. Case insensitive.
    /// Example: "A-*", "TEMPLATE_?".
    /// </summary>
    public IReadOnlyList<string> ExcludedPatterns { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Converts text such as "A-*; TEMP?" into a pattern list. Separators: ';' or line breaks
    /// (a comma is not a separator because layer names may contain commas). Empty entries are dropped and duplicates merged.
    /// </summary>
    public static IReadOnlyList<string> ParsePatterns(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>Inverse of <see cref="ParsePatterns"/>: joins patterns with "; ".</summary>
    public static string FormatPatterns(IEnumerable<string> patterns) => string.Join("; ", patterns);
}

/// <summary>Port through which the layer purge reads and deletes layers.</summary>
public interface ILayerGateway
{
    IReadOnlyList<LayerRecord> GetLayers();

    bool TryDelete(string layerName, out string? failureReason);
}
