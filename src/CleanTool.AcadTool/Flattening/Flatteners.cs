using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CleanTool.AcadTool.Flattening;

/// <summary>
/// Block references (floating blocks). The Z range is measured from the insertion point and the attributes, not from
/// the block CONTENT, so a block with 3D content can still be moved down.
/// The block is moved by -Z (its attributes move with it); any remaining attribute Z offsets are reset separately.
/// Objects inside the block are not handled here: "Include blocks" processes the definition's content separately.
/// </summary>
internal sealed class BlockReferenceFlattener : IEntityFlattener
{
    /// <inheritdoc/>
    public bool CanHandle(Entity entity) => entity is BlockReference;

    /// <inheritdoc/>
    public ZRange? GetZRange(Entity entity, Transaction transaction)
    {
        var block = (BlockReference)entity;
        var values = new List<double> { block.Position.Z };

        foreach (var attribute in Attributes(block, transaction, OpenMode.ForRead))
            values.Add(attribute.Position.Z);

        return ZRange.Of(values.ToArray());
    }

    /// <inheritdoc/>
    public void Flatten(Entity entity, Transaction transaction)
    {
        var block = (BlockReference)entity;

        if (!block.Normal.IsParallelTo(Vector3d.ZAxis))
            throw new InvalidOperationException("Block is rotated out of the XY plane; it cannot be moved to Z=0 without changing its orientation.");

        // Move the block (and its attributes) down.
        if (block.Position.Z != 0)
            block.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, -block.Position.Z)));

        // Attributes that did not move with the block (they have their own Z).
        foreach (var attribute in Attributes(block, transaction, OpenMode.ForWrite))
        {
            if (attribute.Position.Z != 0)
                attribute.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, -attribute.Position.Z)));
        }
    }

    private static IEnumerable<AttributeReference> Attributes(BlockReference block, Transaction transaction, OpenMode mode)
    {
        foreach (ObjectId id in block.AttributeCollection)
        {
            if (id.IsErased) continue;
            if (transaction.GetObject(id, mode) is AttributeReference attribute) yield return attribute;
        }
    }
}

/// <summary>LINE: the Z of both end points is reset directly (the fastest and most exact way).</summary>
internal sealed class LineFlattener : IEntityFlattener
{
    /// <inheritdoc/>
    public bool CanHandle(Entity entity) => entity is Line;

    /// <inheritdoc/>
    public ZRange? GetZRange(Entity entity, Transaction transaction)
    {
        var line = (Line)entity;
        return ZRange.Of(line.StartPoint.Z, line.EndPoint.Z);
    }

    /// <inheritdoc/>
    public void Flatten(Entity entity, Transaction transaction)
    {
        var line = (Line)entity;
        line.StartPoint = new Point3d(line.StartPoint.X, line.StartPoint.Y, 0);
        line.EndPoint = new Point3d(line.EndPoint.X, line.EndPoint.Y, 0);
    }
}

/// <summary>LWPOLYLINE whose plane is parallel to XY: it has a single Elevation value.</summary>
internal sealed class LwPolylineFlattener : IEntityFlattener
{
    public bool CanHandle(Entity entity) =>
        entity is Polyline pl && pl.Normal.IsParallelTo(Vector3d.ZAxis);

    /// <inheritdoc/>
    public ZRange? GetZRange(Entity entity, Transaction transaction)
    {
        // With normal -Z the WCS Z is -Elevation; the absolute value is enough to check flatness.
        var z = ((Polyline)entity).Elevation;
        return ZRange.Of(z);
    }

    /// <inheritdoc/>
    public void Flatten(Entity entity, Transaction transaction) => ((Polyline)entity).Elevation = 0;
}

/// <summary>3D POLYLINE: each vertex is a separate object and is reset one by one.</summary>
internal sealed class Polyline3dFlattener : IEntityFlattener
{
    /// <inheritdoc/>
    public bool CanHandle(Entity entity) => entity is Polyline3d;

    /// <inheritdoc/>
    public ZRange? GetZRange(Entity entity, Transaction transaction) => ZRange.FromExtents(entity);

    /// <inheritdoc/>
    public void Flatten(Entity entity, Transaction transaction)
    {
        foreach (ObjectId vertexId in (Polyline3d)entity)
        {
            var vertex = (PolylineVertex3d)transaction.GetObject(vertexId, OpenMode.ForWrite);
            vertex.Position = new Point3d(vertex.Position.X, vertex.Position.Y, 0);
        }
    }
}

/// <summary>SPLINE: the fit points are reset if present, otherwise the control points.</summary>
internal sealed class SplineFlattener : IEntityFlattener
{
    /// <inheritdoc/>
    public bool CanHandle(Entity entity) => entity is Spline;

    /// <inheritdoc/>
    public ZRange? GetZRange(Entity entity, Transaction transaction) => ZRange.FromExtents(entity);

    /// <inheritdoc/>
    public void Flatten(Entity entity, Transaction transaction)
    {
        var spline = (Spline)entity;

        if (spline.HasFitData)
        {
            for (var i = 0; i < spline.NumFitPoints; i++)
            {
                var p = spline.GetFitPointAt(i);
                spline.SetFitPointAt(i, new Point3d(p.X, p.Y, 0));
            }
            return;
        }

        for (var i = 0; i < spline.NumControlPoints; i++)
        {
            var p = spline.GetControlPointAt(i);
            spline.SetControlPointAt(i, new Point3d(p.X, p.Y, 0));
        }
    }
}

/// <summary>
/// Fallback strategy for every other type:
/// - If the object lies in one plane parallel to XY (circle, arc, text, block, hatch, ...) it is moved by -Z.
///   The move also carries block attributes along.
/// - Otherwise a projection onto XY is attempted; types that do not support it fail and are reported.
/// </summary>
internal sealed class GenericFlattener : IEntityFlattener
{
    private static readonly Matrix3d ProjectToXy =
        Matrix3d.Projection(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis);

    private readonly double _tolerance;

    /// <summary>Creates the fallback flattener.</summary>
    /// <param name="tolerance">Z difference below which an object counts as planar.</param>
    public GenericFlattener(double tolerance)
    {
        _tolerance = tolerance;
    }

    /// <inheritdoc/>
    public bool CanHandle(Entity entity) => true;

    /// <inheritdoc/>
    public ZRange? GetZRange(Entity entity, Transaction transaction) => ZRange.FromExtents(entity);

    /// <inheritdoc/>
    public void Flatten(Entity entity, Transaction transaction)
    {
        var range = ZRange.FromExtents(entity)
            ?? throw new InvalidOperationException("Entity has no geometric extents.");

        if (range.IsPlanar(_tolerance))
            entity.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, -range.Min)));
        else
            entity.TransformBy(ProjectToXy);
    }
}
