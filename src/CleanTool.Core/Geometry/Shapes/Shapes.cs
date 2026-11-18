namespace CleanTool.Core.Geometry.Shapes;

/// <summary>
/// Platform-independent geometry. Each object kind (Line, Circle, Arc, Polyline, ...) is an IShape record.
/// Adding a kind = a new IShape record + its comparer + a reader in the AutoCAD layer.
/// </summary>
public interface IShape
{
    /// <summary>
    /// Sort (sweep) key. Contract: for two shapes that are EQUAL within the tolerance, the keys must differ by at
    /// most the tolerance. This is what keeps duplicate search away from O(n²).
    /// </summary>
    double SortKey { get; }

    /// <summary>Kind name shown in reports.</summary>
    string Kind { get; }

    /// <summary>
    /// Returns the shape transformed (used to bring block content into world coordinates).
    /// Returns null if the transform would change the kind of shape (e.g. a circle under non-uniform scale becomes an ellipse).
    /// </summary>
    /// <param name="transform">Transformation to apply.</param>
    IShape? TryTransform(Transform3 transform);
}

/// <summary>A straight line segment (AutoCAD LINE).</summary>
/// <param name="Segment">Start and end point (WCS).</param>
public sealed record LineShape(Segment3 Segment) : IShape
{
    /// <inheritdoc/>
    public double SortKey => Segment.MinX;
    /// <inheritdoc/>
    public string Kind => "Line";

    /// <inheritdoc/>
    public IShape? TryTransform(Transform3 t) => new LineShape(new Segment3(t.Apply(Segment.Start), t.Apply(Segment.End)));
}

/// <summary>A full circle. Only valid when its plane is parallel to XY.</summary>
/// <param name="Center">Center point (WCS).</param>
/// <param name="Radius">Radius in drawing units.</param>
public sealed record CircleShape(Point3 Center, double Radius) : IShape
{
    /// <inheritdoc/>
    public double SortKey => Center.X;
    /// <inheritdoc/>
    public string Kind => "Circle";

    public IShape? TryTransform(Transform3 t) =>
        t.IsPlanarSimilarity(out var scale, out _) ? new CircleShape(t.Apply(Center), Radius * scale) : null;
}

/// <summary>
/// An arc, defined by its start, end and midpoint. The midpoint distinguishes the minor and major arc with the same
/// end points and allows comparison independent of direction (normal +Z / -Z).
/// </summary>
public sealed record ArcShape(Point3 Center, double Radius, Point3 StartPoint, Point3 EndPoint, Point3 MidPoint) : IShape
{
    /// <inheritdoc/>
    public double SortKey => Math.Min(MidPoint.X, Math.Min(StartPoint.X, EndPoint.X));
    /// <inheritdoc/>
    public string Kind => "Arc";

    /// <summary>Transforms the end points and the midpoint; swapped ends after mirroring are already allowed by the comparer.</summary>
    public IShape? TryTransform(Transform3 t) =>
        t.IsPlanarSimilarity(out var scale, out _)
            ? new ArcShape(t.Apply(Center), Radius * scale, t.Apply(StartPoint), t.Apply(EndPoint), t.Apply(MidPoint))
            : null;
}

/// <param name="Position">WCS position (including elevation).</param>
/// <param name="Bulge">Bulge of the segment from this vertex to the next one (0 = straight).</param>
public readonly record struct PolylineVertex(Point3 Position, double Bulge);

/// <summary>One polyline segment: a directed edge.</summary>
public readonly record struct PolylineEdge(Point3 Start, Point3 End, double Bulge)
{
    /// <summary>Reverse direction: the ends swap and the bulge changes sign so the arc stays the same.</summary>
    public PolylineEdge Reversed() => new(End, Start, -Bulge);

    public bool IsEqualTo(PolylineEdge other, Tolerance tolerance) =>
        Start.IsEqualTo(other.Start, tolerance)
        && End.IsEqualTo(other.End, tolerance)
        && Math.Abs(Bulge - other.Bulge) <= tolerance.Value;
}

/// <summary>
/// A polyline (LWPOLYLINE, 2D or 3D POLYLINE) as a list of vertices with bulges.
/// All polyline kinds are read into this one model so they can be compared with each other.
/// </summary>
/// <param name="Vertices">Vertices in drawing order (WCS).</param>
/// <param name="IsClosed">Whether the last vertex connects back to the first.</param>
public sealed record PolylineShape(IReadOnlyList<PolylineVertex> Vertices, bool IsClosed) : IShape
{
    /// <inheritdoc/>
    public double SortKey => Vertices.Count == 0 ? 0 : Vertices.Min(v => v.Position.X);
    /// <inheritdoc/>
    public string Kind => "Polyline";

    /// <summary>
    /// A polyline with straight segments stays a polyline under any affine transform. With arc segments an XY
    /// similarity is required; mirroring reverses arc direction, so the bulge changes sign.
    /// </summary>
    public IShape? TryTransform(Transform3 t)
    {
        var hasArcs = Vertices.Any(v => v.Bulge != 0);
        var bulgeSign = 1.0;

        if (hasArcs)
        {
            if (!t.IsPlanarSimilarity(out _, out var mirrored)) return null;
            if (mirrored) bulgeSign = -1.0;
        }

        return new PolylineShape(
            Vertices.Select(v => new PolylineVertex(t.Apply(v.Position), v.Bulge * bulgeSign)).ToList(),
            IsClosed);
    }

    /// <summary>Edge list. For a closed polyline the edge from the last vertex back to the first is included.</summary>
    public IReadOnlyList<PolylineEdge> GetEdges()
    {
        var n = Vertices.Count;
        if (n < 2) return Array.Empty<PolylineEdge>();

        var count = IsClosed ? n : n - 1;
        var edges = new List<PolylineEdge>(count);
        for (var i = 0; i < count; i++)
            edges.Add(new PolylineEdge(Vertices[i].Position, Vertices[(i + 1) % n].Position, Vertices[i].Bulge));

        return edges;
    }

    /// <summary>
    /// Simplified copy used for comparison:
    /// - Consecutive vertices at the same point (zero-length segments) are reduced to one vertex.
    /// - A polyline whose last point lies on its first point is treated as closed and the repeated last vertex is dropped.
    ///   A polyline closed with "Close" and one closed by snapping to the start point therefore compare as equal.
    /// </summary>
    /// <param name="tolerance">Distance within which points are considered the same.</param>
    public PolylineShape Normalize(Tolerance tolerance)
    {
        var vertices = new List<PolylineVertex>(Vertices.Count);
        for (var i = 0; i < Vertices.Count; i++)
        {
            // Drop this vertex if it coincides with the next one; the segment's bulge continues on the next vertex.
            var isLast = i == Vertices.Count - 1;
            if (!isLast && Vertices[i].Position.IsEqualTo(Vertices[i + 1].Position, tolerance)) continue;
            vertices.Add(Vertices[i]);
        }

        var closed = IsClosed;
        if (vertices.Count >= 3 && vertices[0].Position.IsEqualTo(vertices[^1].Position, tolerance))
        {
            // The closing edge's bulge lives on the previous vertex, so the repeated last point can be dropped safely.
            vertices.RemoveAt(vertices.Count - 1);
            closed = true;
        }

        return new PolylineShape(vertices, closed);
    }
}

/// <summary>
/// A block reference. Two blocks inserted with the same definition, position, rotation, scale and attribute values are copies.
/// </summary>
/// <param name="DefinitionKey">
/// Identity of the effective block definition (for dynamic blocks the *U copy that carries the parameter values).
/// Two dynamic blocks with different parameters are therefore different.
/// </param>
/// <param name="Position">Insertion point (WCS).</param>
/// <param name="Rotation">Rotation in radians.</param>
/// <param name="ScaleX">X scale factor.</param>
/// <param name="ScaleY">Y scale factor.</param>
/// <param name="ScaleZ">Z scale factor.</param>
/// <param name="Normal">Extrusion direction (a vector stored as Point3).</param>
/// <param name="Attributes">"TAG=value" list sorted by tag.</param>
public sealed record BlockShape(
    string DefinitionKey,
    Point3 Position,
    double Rotation,
    double ScaleX,
    double ScaleY,
    double ScaleZ,
    Point3 Normal,
    IReadOnlyList<string> Attributes) : IShape
{
    /// <inheritdoc/>
    public double SortKey => Position.X;
    /// <inheritdoc/>
    public string Kind => "Block";

    /// <summary>Nested blocks are not compared as a whole; their content is transformed and compared instead.</summary>
    public IShape? TryTransform(Transform3 t) => null;
}