namespace CleanTool.Core.Geometry;

/// <summary>
/// A straight segment between two points. It is undirected: A-B and B-A are the same segment.
/// </summary>
public readonly record struct Segment3(Point3 Start, Point3 End)
{
    /// <summary>Distance between the end points.</summary>
    public double Length => Start.DistanceTo(End);

    /// <summary>Smallest X of the two end points. Used as the sort key when searching for duplicates.</summary>
    public double MinX => Math.Min(Start.X, End.X);

    /// <summary>
    /// Are the two segments equal within the tolerance? Direction does not matter (a reversed line is equal).
    /// </summary>
    public bool IsEqualTo(Segment3 other, Tolerance tolerance)
    {
        var sameDirection = Start.IsEqualTo(other.Start, tolerance) && End.IsEqualTo(other.End, tolerance);
        if (sameDirection) return true;

        return Start.IsEqualTo(other.End, tolerance) && End.IsEqualTo(other.Start, tolerance);
    }
}
