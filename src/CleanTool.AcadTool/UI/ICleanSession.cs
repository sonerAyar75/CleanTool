using CleanTool.AcadTool.Composition;
using CleanTool.Core.Pipeline;

namespace CleanTool.AcadTool.UI;

/// <summary>One row in the block list.</summary>
/// <param name="Name">Block name (for dynamic blocks the name of the base definition).</param>
/// <param name="HasClosedOutline">Does it contain a closed straight polyline or circle? (can act as an inside-trimming outline)</param>
/// <param name="InSelection">Does the selection contain an insert of this block?</param>
internal sealed record BlockListItem(string Name, bool HasClosedOutline, bool InSelection);

/// <summary>One row in the layer list.</summary>
/// <param name="Name">Layer name.</param>
/// <param name="InSelection">Does the selection contain a line or polyline on this layer? (a wall layer candidate)</param>
internal sealed record LayerListItem(string Name, bool InSelection);

/// <summary>Lets options pages read information from the drawing (e.g. the block list). Pages cannot see anything beyond this.</summary>
internal interface IOptionsHost
{
    IReadOnlyList<BlockListItem> GetBlocks();

    /// <summary>Layers of the drawing (xref-dependent layers excluded); layers with lines in the selection are flagged.</summary>
    IReadOnlyList<LayerListItem> GetLayers();
}

/// <summary>
/// The dialog's only door to AutoCAD: select objects, run the checked operations.
/// The form does not know AutoCAD types, which keeps its logic independent of AutoCAD.
/// </summary>
internal interface ICleanSession : IOptionsHost
{
    bool HasSelection { get; }

    string SelectionDescription { get; }

    /// <summary>
    /// Hides the dialog and asks the user to select objects (like "Select objects" in HATCH).
    /// If cancelled, the current selection is kept and false is returned.
    /// </summary>
    bool SelectObjects(IntPtr ownerWindow);

    CleanReport Run(IReadOnlyList<string> operationIds, bool dryRun, ToolSettings settings);
}

/// <summary>Run signature used by flows without a dialog, such as -CT_CLEAN.</summary>
internal delegate CleanReport CleanAction(IReadOnlyList<string> operationIds, bool dryRun, ToolSettings settings);
