using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CleanTool.AcadTool.Infrastructure;
using CleanTool.AcadTool.ShapeReading;
using CleanTool.Core.Geometry.Shapes;
using CleanTool.Core.Model;
using CleanTool.Core.Operations.BlockTrim;
using CleanTool.Core.Operations.Duplicates;

namespace CleanTool.AcadTool.Gateways;

/// <summary>
/// Shared AutoCAD gateway of the shape-based operations (Duplicate and BlockTrim). Reads every type that has a reader.
///
/// Objects inside dynamic blocks are skipped for duplicate removal: different visibility states or actions reuse the
/// same geometry on purpose, and removing a "copy" would break the block. Z = 0 and Thickness still apply there.
/// </summary>
internal sealed class AcadShapeGateway : IDuplicateGateway, IBlockTrimGateway
{
    private readonly AcadSession _session;
    private readonly ShapeReaderRegistry _readers;
    private readonly Dictionary<ObjectId, bool> _dynamicOwners = new();

    /// <summary>Creates the gateway for one session.</summary>
    /// <param name="session">Active document, transaction and scope.</param>
    /// <param name="readers">Readers that convert entities into Core shapes.</param>
    public AcadShapeGateway(AcadSession session, ShapeReaderRegistry readers)
    {
        _session = session;
        _readers = readers;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ShapeRecord> GetShapes()
    {
        var transaction = _session.Transaction;
        var shapes = new List<ShapeRecord>();

        foreach (var id in _session.GetEntityIds())
        {
            // Checking the type without opening the object saves a lot of time on large drawings.
            if (id.IsErased || !_readers.TryGet(id, out var reader)) continue;

            var entity = (Entity)transaction.GetObject(id, OpenMode.ForRead);
            if (IsInsideDynamicBlock(entity.OwnerId, transaction)) continue;

            shapes.Add(new ShapeRecord(
                AcadSession.ToEntityHandle(id),
                AcadSession.ToEntityHandle(entity.OwnerId),
                reader.Read(entity, transaction),
                PropertyReader.Read(entity),
                _session.IsLayerLocked(entity.LayerId)));
        }

        return shapes;
    }

    /// <summary>Block content in the coordinates of the space that holds each insert (for the block overlap step).</summary>
    public IReadOnlyList<ShapeRecord> GetBlockContentShapes() => new BlockContentReader(_session, _readers).Read();

    private bool IsInsideDynamicBlock(ObjectId ownerId, Transaction transaction)
    {
        if (_dynamicOwners.TryGetValue(ownerId, out var isDynamic)) return isDynamic;

        isDynamic = transaction.GetObject(ownerId, OpenMode.ForRead) is BlockTableRecord { IsDynamicBlock: true };
        _dynamicOwners[ownerId] = isDynamic;
        return isDynamic;
    }

    /// <summary>
    /// Trimming: the pieces are added to the same space with the original's properties (layer, color, linetype,
    /// thickness, ...) and the original is erased. Line → Line; LWPolyline/2D polyline → LWPolyline; 3D polyline → 3D polyline.
    /// Note: the new objects get new handles (the original's handle/XData is not carried over).
    /// </summary>
    public bool TryReplace(EntityHandle handle, IReadOnlyList<IShape> pieces, out string? failureReason)
    {
        try
        {
            var transaction = _session.Transaction;
            var original = (Entity)transaction.GetObject(_session.ToObjectId(handle), OpenMode.ForWrite);
            var owner = (BlockTableRecord)transaction.GetObject(original.OwnerId, OpenMode.ForWrite);

            foreach (var piece in pieces)
            {
                var created = CreatePiece(original, piece);
                created.SetPropertiesFrom(original);
                owner.AppendEntity(created);
                transaction.AddNewlyCreatedDBObject(created, true);
            }

            original.Erase();

            failureReason = null;
            return true;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryAddClosedPolyline(EntityHandle propertySource, IReadOnlyList<CleanTool.Core.Geometry.Point3> vertices, out string? failureReason)
    {
        try
        {
            if (vertices.Count < 3) throw new ArgumentException("A closed polyline needs at least 3 vertices.", nameof(vertices));

            var transaction = _session.Transaction;
            var source = (Entity)transaction.GetObject(_session.ToObjectId(propertySource), OpenMode.ForRead);
            var owner = (BlockTableRecord)transaction.GetObject(source.OwnerId, OpenMode.ForWrite);

            var polyline = new Polyline();
            for (var i = 0; i < vertices.Count; i++)
                polyline.AddVertexAt(i, new Point2d(vertices[i].X, vertices[i].Y), 0, 0, 0);

            polyline.Elevation = vertices[0].Z;
            polyline.Closed = true;
            polyline.SetPropertiesFrom(source);

            owner.AppendEntity(polyline);
            transaction.AddNewlyCreatedDBObject(polyline, true);

            failureReason = null;
            return true;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            return false;
        }
    }

    private static Entity CreatePiece(Entity original, IShape piece)
    {
        switch (piece)
        {
            case LineShape line:
                var newLine = new Line(ToPoint(line.Segment.Start), ToPoint(line.Segment.End));
                if (original is Line originalLine) newLine.Thickness = originalLine.Thickness;
                return newLine;

            case PolylineShape polyline when original is Polyline3d:
                var points = new Point3dCollection();
                foreach (var vertex in polyline.Vertices) points.Add(ToPoint(vertex.Position));
                return new Polyline3d(Poly3dType.SimplePoly, points, polyline.IsClosed);

            case PolylineShape polyline:
                // Bulges in Core are relative to WCS (normal +Z); the new polyline is created with normal +Z as well.
                var lw = new Polyline();
                for (var i = 0; i < polyline.Vertices.Count; i++)
                {
                    var v = polyline.Vertices[i];
                    lw.AddVertexAt(i, new Point2d(v.Position.X, v.Position.Y), v.Bulge, 0, 0);
                }

                lw.Elevation = polyline.Vertices[0].Position.Z;
                lw.Closed = polyline.IsClosed;
                return lw;

            default:
                throw new InvalidOperationException($"Cannot create a {piece.Kind} piece.");
        }
    }

    private static Point3d ToPoint(CleanTool.Core.Geometry.Point3 p) => new(p.X, p.Y, p.Z);

    /// <inheritdoc/>
    public bool TryErase(EntityHandle handle, out string? failureReason)
    {
        try
        {
            var entity = (Entity)_session.Transaction.GetObject(_session.ToObjectId(handle), OpenMode.ForWrite);
            entity.Erase();

            failureReason = null;
            return true;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            return false;
        }
    }
}
