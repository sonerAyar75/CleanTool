using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CleanTool.AcadTool.Infrastructure;
using CleanTool.AcadTool.UI;
using CleanTool.Core.Pipeline;

namespace CleanTool.AcadTool.Composition;

/// <summary>AutoCAD implementation of ICleanSession: one document and the selection in it.</summary>
internal sealed class AcadCleanSession : ICleanSession
{
    private readonly Document _document;
    private readonly CleanRunner _runner;
    private SelectionScope _selection;

    /// <summary>Creates a session for one document.</summary>
    /// <param name="document">The active document.</param>
    /// <param name="runner">Runs the tool's operations.</param>
    /// <param name="initialSelection">Objects selected before the command (pickfirst), or null.</param>
    public AcadCleanSession(Document document, CleanRunner runner, SelectionScope? initialSelection)
    {
        _document = document;
        _runner = runner;
        _selection = initialSelection ?? SelectionScope.Empty;
    }

    /// <inheritdoc/>
    public bool HasSelection => _selection.Count > 0;

    /// <inheritdoc/>
    public string SelectionDescription => _selection.Description;

    /// <inheritdoc/>
    public bool SelectObjects(IntPtr ownerWindow)
    {
        // Hides the modal dialog and brings it back when the selection is done.
        using var interaction = _document.Editor.StartUserInteraction(ownerWindow);

        var selection = SelectionScope.PromptNew(_document.Editor);
        interaction.End();

        if (selection is null) return false;

        _selection = selection;
        return true;
    }

    /// <summary>
    /// Named blocks in the drawing (layouts, xrefs, dependent and anonymous definitions excluded).
    /// Blocks in the selection and blocks with a closed outline are flagged.
    /// </summary>
    public IReadOnlyList<BlockListItem> GetBlocks()
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartOpenCloseTransaction();

        var inSelection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _selection.Ids)
        {
            if (id.IsErased || transaction.GetObject(id, OpenMode.ForRead) is not BlockReference block) continue;
            inSelection.Add(NameOf(block, transaction));
        }

        var items = new List<BlockListItem>();
        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId id in blockTable)
        {
            var definition = (BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead);
            if (definition.IsLayout || definition.IsAnonymous || definition.IsFromExternalReference
                || definition.IsFromOverlayReference || definition.IsDependent) continue;

            items.Add(new BlockListItem(definition.Name, HasClosedOutline(definition, transaction), inSelection.Contains(definition.Name)));
        }

        transaction.Commit();
        return items;
    }

    /// <inheritdoc/>
    public IReadOnlyList<LayerListItem> GetLayers()
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartOpenCloseTransaction();

        var inSelection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _selection.Ids)
        {
            if (id.IsErased || id.ObjectClass.Name is not ("AcDbLine" or "AcDbPolyline" or "AcDb2dPolyline" or "AcDb3dPolyline")) continue;
            inSelection.Add(((Entity)transaction.GetObject(id, OpenMode.ForRead)).Layer);
        }

        var items = new List<LayerListItem>();
        var layers = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        foreach (ObjectId id in layers)
        {
            var layer = (LayerTableRecord)transaction.GetObject(id, OpenMode.ForRead);
            if (layer.IsDependent) continue;
            items.Add(new LayerListItem(layer.Name, inSelection.Contains(layer.Name)));
        }

        transaction.Commit();
        return items;
    }

    /// <summary>Does the definition contain an object that can act as an inside-trimming outline? (closed polyline without arcs, or a circle)</summary>
    private static bool HasClosedOutline(BlockTableRecord definition, Transaction transaction)
    {
        foreach (ObjectId id in definition)
        {
            if (id.IsErased) continue;

            switch (id.ObjectClass.Name)
            {
                case "AcDbCircle":
                    return true;

                case "AcDbPolyline":
                    var pl = (Polyline)transaction.GetObject(id, OpenMode.ForRead);
                    if (IsClosed(pl) && Enumerable.Range(0, pl.NumberOfVertices).All(i => pl.GetBulgeAt(i) == 0)) return true;
                    break;

                case "AcDb2dPolyline":
                case "AcDb3dPolyline":
                    if (((Curve)transaction.GetObject(id, OpenMode.ForRead)).Closed) return true;
                    break;
            }
        }

        return false;
    }

    /// <summary>Closed with "Close" or by its last point lying on its first point.</summary>
    private static bool IsClosed(Polyline pl) =>
        pl.NumberOfVertices >= 3
        && (pl.Closed || pl.GetPoint2dAt(0).GetDistanceTo(pl.GetPoint2dAt(pl.NumberOfVertices - 1)) < 1e-6);

    private static string NameOf(BlockReference block, Transaction transaction) =>
        block.IsDynamicBlock
            ? ((BlockTableRecord)transaction.GetObject(block.DynamicBlockTableRecord, OpenMode.ForRead)).Name
            : block.Name;

    /// <summary>
    /// A Run result is also written to the command line, in addition to the results panel, so it stays in the F2
    /// text window and in the command history. A preview is shown only in the panel (it changes nothing).
    /// </summary>
    public CleanReport Run(IReadOnlyList<string> operationIds, bool dryRun, ToolSettings settings)
    {
        var report = _runner.Run(_document, _selection, operationIds, dryRun, settings);
        if (!dryRun) CommandLine.WriteReport(_document.Editor, report);
        return report;
    }
}
