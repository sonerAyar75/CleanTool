using CleanTool.AcadTool.Composition;
using CleanTool.Core.Hosting;

namespace CleanTool.AcadTool.Tools.Clean;

/// <summary>
/// Cleanup tool: Thickness = 0, Z = 0, duplicate removal, unused layers.
/// Commands: CT_CLEAN / TTC (dialog), -CT_CLEAN / -TTC (command line), CT_THICKNESS, CT_FLATTEN, CT_DUPLICATES, CT_LAYERS.
///
/// Owns its operation list, settings and commands; never references other tools.
/// A new cleanup operation = one more Register line in <see cref="Initialize"/>
/// (the dialog and -CT_CLEAN pick it up automatically).
/// </summary>
internal sealed class CleanModule : IToolModule
{
    /// <inheritdoc/>
    public string Name => "Clean";

    /// <summary>The Clean tool's operations.</summary>
    public OperationRegistry Registry { get; private set; } = null!;

    /// <summary>Runs the Clean tool's operations.</summary>
    public CleanRunner Runner { get; private set; } = null!;

    /// <summary>The Clean tool's settings for this AutoCAD session.</summary>
    public SettingsStore Settings { get; private set; } = null!;

    /// <inheritdoc/>
    public void Initialize()
    {
        // Execution order is decided by each operation's CleanStage, not by registration order
        // (Thickness → Flatten → Duplicates → Layers). Registration order is only the display order in the dialog.
        Registry = new OperationRegistry()
            .Register(new ThicknessOperationFactory())
            .Register(new FlattenOperationFactory())
            .Register(new DuplicateOperationFactory())
            .Register(new LayerPurgeOperationFactory());

        Runner = new CleanRunner(Registry);
        Settings = new SettingsStore(ToolSettings.Default, Registry.All.Select(f => f.Id));
    }
}
