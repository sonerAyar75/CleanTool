using CleanTool.AcadTool.Composition;
using CleanTool.AcadTool.Gateways;
using CleanTool.AcadTool.Infrastructure;
using CleanTool.AcadTool.ShapeReading;
using CleanTool.AcadTool.UI;
using CleanTool.Core.Abstractions;
using CleanTool.Core.Hosting;
using CleanTool.Core.Operations.BlockTrim;

namespace CleanTool.AcadTool.Tools.WallTrim;

/// <summary>
/// Wall–column tool: cuts loose lines that run over or through blocks (e.g. columns) at the block face.
/// Commands: CT_WALLTRIM / TTW (dialog), -CT_WALLTRIM / -TTW (command line).
///
/// Deliberately separate from cleanup: it edits geometry (cuts lines, creates new objects), is run on a specific
/// area with specific blocks and has its own settings. It never references the Clean module.
/// </summary>
internal sealed class WallTrimModule : IToolModule
{
    /// <inheritdoc/>
    public string Name => "WallTrim";

    /// <summary>The WallTrim tool's operations (one: BlockTrim).</summary>
    public OperationRegistry Registry { get; private set; } = null!;

    /// <summary>Runs the WallTrim operation.</summary>
    public CleanRunner Runner { get; private set; } = null!;

    /// <summary>The WallTrim tool's settings for this AutoCAD session.</summary>
    public SettingsStore Settings { get; private set; } = null!;

    /// <inheritdoc/>
    public void Initialize()
    {
        Registry = new OperationRegistry().Register(new BlockTrimOperationFactory());
        Runner = new CleanRunner(Registry);

        // Include blocks makes no sense for this tool: what gets cut are the loose lines in the space.
        Settings = new SettingsStore(ToolSettings.Default with { Blocks = BlockMode.None }, Registry.All.Select(f => f.Id));
    }

    /// <summary>Dialog customization: titled "WallTrim", without the Include blocks row.</summary>
    public static ToolFormOptions FormOptions { get; } = new("WallTrim", ShowIncludeBlocks: false);
}

/// <summary>Connects the BlockTrim operation with its AutoCAD gateway.</summary>
internal sealed class BlockTrimOperationFactory : IOperationFactory
{
    /// <inheritdoc/>
    public string Id => BlockTrimOperation.OperationId;

    /// <inheritdoc/>
    public string DisplayName => "Trim lines at blocks";

    public string Description =>
        "Cuts loose lines and polylines where they run along block edges, and inside the blocks listed in " +
        "'Trim inside blocks' (e.g. columns). Lines with nothing left are deleted.";

    /// <inheritdoc/>
    public string CommandKeyword => "Trim";

    /// <inheritdoc/>
    public bool UsesSelection => true;

    public ICleanOperation Create(AcadSession session, ToolSettings settings) =>
        new BlockTrimOperation(new AcadShapeGateway(session, ShapeReaderRegistry.CreateDefault()), settings.WallTrim);

    /// <inheritdoc/>
    public IOptionsPage CreateOptionsPage(ToolSettings settings, IOptionsHost host) => new BlockTrimOptionsPage(settings, host);
}
