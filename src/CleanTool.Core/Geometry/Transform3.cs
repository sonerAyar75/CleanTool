namespace CleanTool.Core.Geometry;

/// <summary>
/// Affine 3D transformation (the upper 3x4 part of a 4x4 matrix, row-major). Used to bring block content into
/// world coordinates. The AutoCAD layer converts AutoCAD's BlockTransform matrix into this type.
///
///   | M00 M01 M02 M03 |   x
///   | M10 M11 M12 M13 | · y
///   | M20 M21 M22 M23 |   z
///   |  0   0   0   1  |   1
/// </summary>
public readonly record struct Transform3(
    double M00, double M01, double M02, double M03,
    double M10, double M11, double M12, double M13,
    double M20, double M21, double M22, double M23)
{
    private const double Epsilon = 1e-9;

    /// <summary>The identity transform (changes nothing).</summary>
    public static Transform3 Identity => new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0);

    /// <summary>A pure translation by (x, y, z).</summary>
    public static Transform3 Translation(double x, double y, double z) => new(1, 0, 0, x, 0, 1, 0, y, 0, 0, 1, z);

    /// <summary>Rotation about Z + scale in XY + translation (the typical transform of a block insert).</summary>
    public static Transform3 Insert(double x, double y, double z, double rotation, double scaleX, double scaleY, double scaleZ = 1)
    {
        var c = Math.Cos(rotation);
        var s = Math.Sin(rotation);
        return new Transform3(
            c * scaleX, -s * scaleY, 0, x,
            s * scaleX, c * scaleY, 0, y,
            0, 0, scaleZ, z);
    }

    /// <summary>Applies the transform to a point.</summary>
    public Point3 Apply(Point3 p) => new(
        M00 * p.X + M01 * p.Y + M02 * p.Z + M03,
        M10 * p.X + M11 * p.Y + M12 * p.Z + M13,
        M20 * p.X + M21 * p.Y + M22 * p.Z + M23);

    /// <summary>Composition this ∘ other: <paramref name="o"/> is applied first, then this transform.</summary>
    public Transform3 Multiply(Transform3 o) => new(
        M00 * o.M00 + M01 * o.M10 + M02 * o.M20, M00 * o.M01 + M01 * o.M11 + M02 * o.M21, M00 * o.M02 + M01 * o.M12 + M02 * o.M22, M00 * o.M03 + M01 * o.M13 + M02 * o.M23 + M03,
        M10 * o.M00 + M11 * o.M10 + M12 * o.M20, M10 * o.M01 + M11 * o.M11 + M12 * o.M21, M10 * o.M02 + M11 * o.M12 + M12 * o.M22, M10 * o.M03 + M11 * o.M13 + M12 * o.M23 + M13,
        M20 * o.M00 + M21 * o.M10 + M22 * o.M20, M20 * o.M01 + M21 * o.M11 + M22 * o.M21, M20 * o.M02 + M21 * o.M12 + M22 * o.M22, M20 * o.M03 + M21 * o.M13 + M22 * o.M23 + M23);

    /// <summary>
    /// Is the transform a similarity in the XY plane? (translation + rotation about Z + equal XY scale, mirroring
    /// allowed; the XY plane maps onto the XY plane). Circles and arcs stay circles and arcs only in this case.
    /// </summary>
    /// <param name="scale">XY scale factor (positive).</param>
    /// <param name="mirrored">Whether the transform mirrors in XY (arcs change direction).</param>
    public bool IsPlanarSimilarity(out double scale, out bool mirrored)
    {
        var xLength = Math.Sqrt(M00 * M00 + M10 * M10 + M20 * M20);
        var yLength = Math.Sqrt(M01 * M01 + M11 * M11 + M21 * M21);
        scale = xLength;
        mirrored = M00 * M11 - M01 * M10 < 0;

        var tolerance = Epsilon * Math.Max(1, xLength);
        var staysInXy = Math.Abs(M20) <= tolerance && Math.Abs(M21) <= tolerance;
        var uniform = Math.Abs(xLength - yLength) <= tolerance;
        var orthogonal = Math.Abs(M00 * M01 + M10 * M11) <= tolerance * Math.Max(1, xLength);

        return xLength > 0 && staysInXy && uniform && orthogonal;
    }
}
