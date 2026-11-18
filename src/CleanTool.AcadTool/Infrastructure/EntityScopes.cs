using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace CleanTool.AcadTool.Infrastructure;

/// <summary>
/// Decides which objects the gateways look at: the selection, the whole space, block content, ...
/// Core does not know about this concept; it only works with the objects the gateways hand over.
/// </summary>
internal interface IEntityScope
{
    /// <summary>Short description shown to the user (e.g. "24 objects selected").</summary>
    string Description { get; }

    IEnumerable<ObjectId> GetEntityIds(Transaction transaction, Database database);
}

/// <summary>Whether the content of block definitions is cleaned as well.</summary>
internal enum BlockMode
{
    /// <summary>Only the selected objects (a block reference is treated as a single object).</summary>
    None,

    /// <summary>Also the definitions of the selected block references and the blocks nested in them (dynamic blocks included).</summary>
    SelectedBlocks,

    /// <summary>All eligible block definitions in the drawing, regardless of the selection.</summary>
    AllBlocks
}

/// <summary>All objects in the current space (model space or the active layout).</summary>
internal sealed class CurrentSpaceScope : IEntityScope
{
    /// <inheritdoc/>
    public string Description => "all objects in the current space";

    /// <inheritdoc/>
    public IEnumerable<ObjectId> GetEntityIds(Transaction transaction, Database database)
    {
        var space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForRead);
        foreach (ObjectId id in space)
        {
            if (!id.IsErased) yield return id;
        }
    }
}

/// <summary>Objects selected by the user. Objects erased by an earlier run are skipped.</summary>
internal sealed class SelectionScope : IEntityScope
{
    private readonly IReadOnlyList<ObjectId> _ids;

    /// <summary>Creates a scope from the given object ids.</summary>
    public SelectionScope(IEnumerable<ObjectId> ids)
    {
        _ids = ids.ToList();
    }

    /// <summary>An empty selection.</summary>
    public static SelectionScope Empty { get; } = new(Array.Empty<ObjectId>());

    /// <summary>Number of selected objects.</summary>
    public int Count => _ids.Count;

    /// <summary>The selected object ids (may include objects erased since).</summary>
    public IReadOnlyList<ObjectId> Ids => _ids;

    /// <inheritdoc/>
    public string Description => _ids.Count switch
    {
        0 => "No objects selected",
        1 => "1 object selected",
        _ => $"{_ids.Count} objects selected"
    };

    public IEnumerable<ObjectId> GetEntityIds(Transaction transaction, Database database) =>
        _ids.Where(id => !id.IsErased);

    /// <summary>Objects selected before the command started (pickfirst), or null if there are none.</summary>
    public static SelectionScope? FromImplied(Editor editor)
    {
        var implied = editor.SelectImplied();
        if (implied.Status != PromptStatus.OK || implied.Value is not { Count: > 0 }) return null;

        editor.SetImpliedSelection(Array.Empty<ObjectId>());
        return new SelectionScope(implied.Value.GetObjectIds());
    }

    /// <summary>Asks the user to select objects (ALL selects everything). Returns null if cancelled.</summary>
    public static SelectionScope? PromptNew(Editor editor)
    {
        var options = new PromptSelectionOptions
        {
            MessageForAdding = "\nSelect objects to clean (type ALL for everything): "
        };

        var result = editor.GetSelection(options);
        return result.Status == PromptStatus.OK ? new SelectionScope(result.Value.GetObjectIds()) : null;
    }

    /// <summary>The pickfirst selection if there is one, otherwise a new selection.</summary>
    public static SelectionScope? Prompt(Editor editor) => FromImplied(editor) ?? PromptNew(editor);
}

/// <summary>
/// Decorator: returns the objects of the inner scope plus the objects inside block definitions.
/// Nested blocks are followed too; each definition is processed once.
///
/// For dynamic blocks the base definition (DynamicBlockTableRecord) is processed; the anonymous *U copies are
/// regenerated from it at the end of the run (see <see cref="DynamicDefinitions"/>).
///
/// Definitions skipped for safety: layouts, xrefs and dependent definitions, other anonymous blocks
/// (dimension blocks *D, tables, hatches, ...).
/// </summary>
internal sealed class BlockContentScope : IEntityScope
{
    private static readonly RXClass BlockReferenceClass = RXObject.GetClass(typeof(BlockReference));

    private readonly IEntityScope _inner;
    private readonly BlockMode _mode;
    private readonly HashSet<ObjectId> _dynamicDefinitions = new();

    private BlockContentScope(IEntityScope inner, BlockMode mode)
    {
        _inner = inner;
        _mode = mode;
    }

    /// <summary>Returns the scope unchanged when the mode is BlockMode.None.</summary>
    public static IEntityScope Wrap(IEntityScope inner, BlockMode mode) =>
        mode == BlockMode.None ? inner : new BlockContentScope(inner, mode);

    /// <summary>
    /// Dynamic block definitions that were scanned. If their content changed, their anonymous copies must be
    /// regenerated (BlockTableRecord.UpdateAnonymousBlocks).
    /// </summary>
    public IReadOnlyCollection<ObjectId> DynamicDefinitions => _dynamicDefinitions;

    /// <inheritdoc/>
    public string Description => _mode == BlockMode.AllBlocks
        ? $"{_inner.Description} + all block definitions"
        : $"{_inner.Description} + their block definitions";

    /// <inheritdoc/>
    public IEnumerable<ObjectId> GetEntityIds(Transaction transaction, Database database)
    {
        var pending = new Queue<ObjectId>();
        var visited = new HashSet<ObjectId>();

        foreach (var id in _inner.GetEntityIds(transaction, database))
        {
            yield return id;

            if (_mode == BlockMode.SelectedBlocks && IsBlockReference(id))
                pending.Enqueue(DefinitionOf(id, transaction));
        }

        if (_mode == BlockMode.AllBlocks)
        {
            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId definitionId in blockTable) pending.Enqueue(definitionId);
        }

        while (pending.Count > 0)
        {
            var definitionId = pending.Dequeue();
            if (!visited.Add(definitionId)) continue;

            var definition = (BlockTableRecord)transaction.GetObject(definitionId, OpenMode.ForRead);
            if (!IsEditable(definition)) continue;
            if (definition.IsDynamicBlock) _dynamicDefinitions.Add(definitionId);

            foreach (ObjectId entityId in definition)
            {
                if (entityId.IsErased) continue;
                yield return entityId;

                // Nested blocks
                if (IsBlockReference(entityId))
                    pending.Enqueue(DefinitionOf(entityId, transaction));
            }
        }
    }

    /// <summary>For a dynamic block returns the base definition instead of the *U copy.</summary>
    private static ObjectId DefinitionOf(ObjectId blockReferenceId, Transaction transaction)
    {
        var block = (BlockReference)transaction.GetObject(blockReferenceId, OpenMode.ForRead);
        return block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord;
    }

    private static bool IsBlockReference(ObjectId id) => !id.IsErased && id.ObjectClass.IsDerivedFrom(BlockReferenceClass);

    private static bool IsEditable(BlockTableRecord definition) =>
        !definition.IsLayout
        && !definition.IsFromExternalReference
        && !definition.IsFromOverlayReference
        && !definition.IsDependent
        && !definition.IsAnonymous;
}
