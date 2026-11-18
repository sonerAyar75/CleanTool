using Autodesk.AutoCAD.DatabaseServices;

namespace CleanTool.AcadTool.Flattening;

/// <summary>Z range of an object in WCS.</summary>
internal readonly record struct ZRange(double Min, double Max)
{
    /// <summary>Does the object lie in a single plane parallel to XY?</summary>
    public bool IsPlanar(double tolerance) => Math.Abs(Max - Min) <= tolerance;

    /// <summary>The range spanning the given Z values.</summary>
    public static ZRange Of(params double[] values) => new(values.Min(), values.Max());

    /// <summary>Z range from GeometricExtents. Returns null for unbounded objects (XLine, Ray).</summary>
    public static ZRange? FromExtents(Entity entity)
    {
        try
        {
            var extents = entity.GeometricExtents;
            return new ZRange(extents.MinPoint.Z, extents.MaxPoint.Z);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// Strategy for moving one kind of object to Z = 0 (Strategy pattern).
/// If a type needs special handling, write a new class and add it to <see cref="FlattenerRegistry"/>.
/// </summary>
internal interface IEntityFlattener
{
    bool CanHandle(Entity entity);

    /// <summary>Returns null if the Z range cannot be determined.</summary>
    ZRange? GetZRange(Entity entity, Transaction transaction);

    /// <summary>The entity is passed in already opened for write.</summary>
    void Flatten(Entity entity, Transaction transaction);
}
