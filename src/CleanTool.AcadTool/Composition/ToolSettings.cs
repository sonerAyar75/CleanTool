using CleanTool.AcadTool.Infrastructure;
using CleanTool.Core.Operations.BlockTrim;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Operations.Flatten;
using CleanTool.Core.Operations.Layers;
using CleanTool.Core.Operations.Thickness;

namespace CleanTool.AcadTool.Composition;

/// <summary>
/// Settings of the tools, section by section (each tool uses its own section; each module has its own SettingsStore).
/// Currently fixed defaults; later they can be loaded from a JSON file (only the code creating this record changes).
/// </summary>
internal sealed record ToolSettings
{
    /// <summary>Thickness reset.</summary>
    public ThicknessOptions Thickness { get; init; } = new();

    /// <summary>Flatten (Z = 0).</summary>
    public FlattenOptions Flatten { get; init; } = new();

    /// <summary>Duplicate removal; IgnoredProperties also removes copies on other layers, colors, etc.</summary>
    public DuplicateOptions Duplicates { get; init; } = new();

    /// <summary>Clean the content of block definitions too? (Changing a definition changes all its inserts.)</summary>
    public BlockMode Blocks { get; init; } = BlockMode.SelectedBlocks;

    /// <summary>Unused layer purge.</summary>
    public LayerPurgeOptions Layers { get; init; } = new();

    /// <summary>WallTrim tool: cutting lines that run over or through blocks (columns).</summary>
    public BlockTrimOptions WallTrim { get; init; } = new();

    /// <summary>The built-in defaults.</summary>
    public static ToolSettings Default { get; } = new();
}
