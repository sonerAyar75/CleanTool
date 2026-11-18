using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using CleanTool.AcadTool.Composition;
using CleanTool.AcadTool.Hosting;
using CleanTool.AcadTool.Infrastructure;
using CleanTool.AcadTool.UI;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Operations.Flatten;
using CleanTool.Core.Operations.Layers;
using CleanTool.Core.Operations.Thickness;
using CleanTool.Core.Pipeline;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(CleanTool.AcadTool.Tools.Clean.CleanCommands))]

namespace CleanTool.AcadTool.Tools.Clean;

/// <summary>
/// AutoCAD commands of the Clean tool. There is no business logic here: they collect the selection and decide which UI to open.
/// If the module failed to load, every command reports why and exits (other tools are not affected).
///
/// CT_CLEAN: the dialog opens and objects are selected from it (like OVERKILL / HATCH).
/// -CT_CLEAN and the quick commands: select objects first (pickfirst or ALL), then run.
/// </summary>
public sealed class CleanCommands
{
    private const string Group = "CLEANTOOL";
    private const CommandFlags SelectionFlags = CommandFlags.Modal | CommandFlags.UsePickSet;

    /// <summary>
    /// Opens the dialog first; objects are selected with the "Select objects" button.
    /// Objects selected before the command (pickfirst) become the initial selection.
    /// </summary>
    [CommandMethod(Group, "CT_CLEAN", SelectionFlags)]
    public void CleanWithDialog()
    {
        if (!TryStart(out var document, out var clean)) return;

        var session = new AcadCleanSession(document, clean.Runner, SelectionScope.FromImplied(document.Editor));

        using var form = new CleanToolForm(clean.Registry.All, clean.Settings, session);
        AcApp.ShowModalDialog(form);
    }

    /// <summary>Select objects, then command-line options (no dialog; suitable for scripts).</summary>
    [CommandMethod(Group, "-CT_CLEAN", SelectionFlags)]
    public void CleanFromCommandLine()
    {
        if (!TryStart(out var document, out var clean)) return;

        var scope = SelectionScope.Prompt(document.Editor);
        if (scope is null) return;

        document.Editor.WriteMessage($"\n{scope.Description}.");

        new CommandLineClean(document.Editor, clean.Registry.All, clean.Settings, CreateAction(clean, document, scope)).Run();
    }

    /// <summary>Short alias of CT_CLEAN.</summary>
    [CommandMethod(Group, "TTC", SelectionFlags)]
    public void CleanWithDialogAlias() => CleanWithDialog();

    /// <summary>Short alias of -CT_CLEAN.</summary>
    [CommandMethod(Group, "-TTC", SelectionFlags)]
    public void CleanFromCommandLineAlias() => CleanFromCommandLine();

    // Single-operation quick commands: run directly on the selection with the saved settings.

    /// <summary>CT_THICKNESS: resets the thickness of the selected objects.</summary>
    [CommandMethod(Group, "CT_THICKNESS", SelectionFlags)]
    public void Thickness() => RunOnSelection(ThicknessOperation.OperationId);

    /// <summary>CT_FLATTEN: moves the selected objects to Z = 0.</summary>
    [CommandMethod(Group, "CT_FLATTEN", SelectionFlags)]
    public void Flatten() => RunOnSelection(FlattenOperation.OperationId);

    /// <summary>CT_DUPLICATES: removes duplicates in the selection.</summary>
    [CommandMethod(Group, "CT_DUPLICATES", SelectionFlags)]
    public void Duplicates() => RunOnSelection(DuplicateOperation.OperationId);

    /// <summary>The layer purge applies to the whole drawing; no selection is needed.</summary>
    [CommandMethod(Group, "CT_LAYERS", CommandFlags.Modal)]
    public void PurgeLayers()
    {
        if (!TryStart(out var document, out var clean)) return;

        Report(document, clean.Runner.Run(
            document, new CurrentSpaceScope(), new[] { LayerPurgeOperation.OperationId }, false, clean.Settings.Current));
    }

    private static void RunOnSelection(string operationId)
    {
        if (!TryStart(out var document, out var clean)) return;

        var scope = SelectionScope.Prompt(document.Editor);
        if (scope is null) return;

        Report(document, clean.Runner.Run(document, scope, new[] { operationId }, false, clean.Settings.Current));
    }

    /// <summary>Is there an active document and is the Clean module loaded? Otherwise writes the reason.</summary>
    private static bool TryStart(out Document document, out CleanModule clean)
    {
        clean = null!;
        document = AcApp.DocumentManager.MdiActiveDocument;
        return document is not null && ToolHost.TryGet(document.Editor, out clean);
    }

    private static CleanAction CreateAction(CleanModule clean, Document document, IEntityScope scope) =>
        (ids, dryRun, settings) => clean.Runner.Run(document, scope, ids, dryRun, settings);

    private static void Report(Document document, CleanReport report) => CommandLine.WriteReport(document.Editor, report);
}
