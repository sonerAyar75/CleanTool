using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CleanTool.AcadTool.Infrastructure;
using CleanTool.Core.Geometry;
using CleanTool.Core.Model;
using CleanTool.Core.Operations.Duplicates;

namespace CleanTool.AcadTool.ShapeReading;

/// <summary>
/// Reads the content of the top-level block references in scope, transformed into the coordinates of the space
/// (model/layout) that holds each insert. This lets a line inside a block be compared with a loose line drawn over it.
///
/// - Nested blocks are read by chaining their transforms (at most <see cref="MaxDepth"/> levels).
/// - Effective properties are resolved: objects on layer 0 / with ByBlock properties take the insert's layer and values.
/// - Xrefs and MINSERTs are not read. Objects whose shape the transform would distort (circles under non-uniform scale) are skipped.
/// - Whether erasing from the definition is safe (<see cref="BlockContentInfo.CannotRemoveReason"/>) is decided here.
/// </summary>
internal sealed class BlockContentReader
{
    private const int MaxDepth = 16;
    private const string BlockReferenceClass = "AcDbBlockReference"; // MINSERT ("AcDbMInsertBlock") is excluded on purpose

    private readonly AcadSession _session;
    private readonly ShapeReaderRegistry _readers;
    private readonly Dictionary<ObjectId, int> _insertCounts = new();

    /// <summary>Creates the reader for one session.</summary>
    public BlockContentReader(AcadSession session, ShapeReaderRegistry readers)
    {
        _session = session;
        _readers = readers;
    }

    /// <summary>Reads the content of every top-level block reference in scope (see the class description).</summary>
    public IReadOnlyList<ShapeRecord> Read()
    {
        var transaction = _session.Transaction;
        var result = new List<ShapeRecord>();

        foreach (var id in _session.GetEntityIds())
        {
            if (id.IsErased || id.ObjectClass.Name != BlockReferenceClass) continue;

            var block = (BlockReference)transaction.GetObject(id, OpenMode.ForRead);

            // Only inserts that sit in a space (model/layout). Nested blocks inside definitions are chained in Visit.
            var owner = (BlockTableRecord)transaction.GetObject(block.OwnerId, OpenMode.ForRead);
            if (!owner.IsLayout) continue;

            var context = new InsertContext(
                Top: block,
                Name: DisplayName(block, transaction),
                Space: AcadSession.ToEntityHandle(block.OwnerId));

            Visit(context, block, block.BlockTransform, PropertyReader.Read(block), depth: 0, result, transaction);
        }

        return result;
    }

    private void Visit(InsertContext context, BlockReference current, Matrix3d transform, DisplayProperties insertProperties,
        int depth, List<ShapeRecord> result, Transaction transaction)
    {
        if (depth >= MaxDepth) return;

        var definition = (BlockTableRecord)transaction.GetObject(current.BlockTableRecord, OpenMode.ForRead);
        if (definition.IsFromExternalReference || definition.IsFromOverlayReference || definition.IsDependent) return;

        var cannotRemove = CannotRemoveReason(current, definition, depth);
        var coreTransform = ToCore(transform);
        var info = new BlockContentInfo(context.Name, AcadSession.ToEntityHandle(context.Top.ObjectId), cannotRemove);

        foreach (ObjectId id in definition)
        {
            if (id.IsErased) continue;

            if (id.ObjectClass.Name == BlockReferenceClass)
            {
                var nested = (BlockReference)transaction.GetObject(id, OpenMode.ForRead);
                var nestedProperties = PropertyReader.Read(nested).ResolveInBlock(insertProperties);
                Visit(context, nested, transform * nested.BlockTransform, nestedProperties, depth + 1, result, transaction);
                continue;
            }

            if (!_readers.TryGet(id, out var reader)) continue;

            var entity = (Entity)transaction.GetObject(id, OpenMode.ForRead);
            if (!entity.Visible) continue;

            var shape = reader.Read(entity, transaction).TryTransform(coreTransform);
            if (shape is null) continue; // e.g. a circle under non-uniform scale: cannot be compared safely

            result.Add(new ShapeRecord(
                AcadSession.ToEntityHandle(id),
                context.Space,
                shape,
                PropertyReader.Read(entity).ResolveInBlock(insertProperties),
                _session.IsLayerLocked(entity.LayerId),
                info));
        }
    }

    /// <summary>Why is erasing the object from the definition unsafe? null = safe.</summary>
    private string? CannotRemoveReason(BlockReference insert, BlockTableRecord definition, int depth)
    {
        if (depth > 0) return "it is inside a nested block";
        if (insert.IsDynamicBlock || definition.IsAnonymous) return "dynamic and anonymous blocks are not edited";

        if (!_insertCounts.TryGetValue(definition.ObjectId, out var count))
        {
            count = definition.GetBlockReferenceIds(true, false).Count;
            _insertCounts[definition.ObjectId] = count;
        }

        return count > 1 ? $"block is used {count} times" : null;
    }

    private static string DisplayName(BlockReference block, Transaction transaction)
    {
        if (!block.IsDynamicBlock) return block.Name;

        // The name of the base definition the user knows, not of the *U copy.
        var definition = (BlockTableRecord)transaction.GetObject(block.DynamicBlockTableRecord, OpenMode.ForRead);
        return definition.Name;
    }

    private static Transform3 ToCore(Matrix3d m) => new(
        m[0, 0], m[0, 1], m[0, 2], m[0, 3],
        m[1, 0], m[1, 1], m[1, 2], m[1, 3],
        m[2, 0], m[2, 1], m[2, 2], m[2, 3]);

    private sealed record InsertContext(BlockReference Top, string Name, EntityHandle Space);
}
