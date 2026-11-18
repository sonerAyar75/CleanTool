namespace CleanTool.Core.Geometry;

/// <summary>
/// A 3D point independent of AutoCAD. The AutoCAD layer converts between Point3d and Point3.
/// </summary>
public readonly record struct Point3(double X, double Y, double Z)
{
    /// <summary>The point (0, 0, 0).</summary>
    public static Point3 Origin => new(0, 0, 0);

    /// <summary>Euclidean distance to <paramref name="other"/>.</summary>
    public double DistanceTo(Point3 other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Is the distance to <paramref name="other"/> within the tolerance?</summary>
    public bool IsEqualTo(Point3 other, Tolerance tolerance) => DistanceTo(other) <= tolerance.Value;

    /// <summary>Returns a copy with Z set to zero.</summary>
    public Point3 WithZeroZ() => this with { Z = 0 };
}
