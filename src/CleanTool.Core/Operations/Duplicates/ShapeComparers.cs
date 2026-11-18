using System.Diagnostics.CodeAnalysis;
using CleanTool.Core.Geometry;
using CleanTool.Core.Geometry.Shapes;

namespace CleanTool.Core.Operations.Duplicates;

/// <summary>Decides whether two shapes of the same kind are equal within a tolerance (Strategy pattern).</summary>
public interface IShapeComparer
{
    Type ShapeType { get; }

    bool AreEqual(IShape a, IShape b, Tolerance tolerance);
}

/// <summary>Base class that performs the type cast in one place.</summary>
public abstract class ShapeComparer<TShape> : IShapeComparer where TShape : IShape
{
    /// <inheritdoc/>
    public Type ShapeType => typeof(TShape);

    /// <inheritdoc/>
    public bool AreEqual(IShape a, IShape b, Tolerance tolerance) => AreEqual((TShape)a, (TShape)b, tolerance);

    /// <summary>Compares two shapes of type <typeparamref name="TShape"/>.</summary>
    protected abstract bool AreEqual(TShape a, TShape b, Tolerance tolerance);
}

/// <summary>Lines are equal when both end points match (in either direction).</summary>
public sealed class LineComparer : ShapeComparer<LineShape>
{
    protected override bool AreEqual(LineShape a, LineShape b, Tolerance t) => a.Segment.IsEqualTo(b.Segment, t);
}

/// <summary>Circles are equal when center and radius match.</summary>
public sealed class CircleComparer : ShapeComparer<CircleShape>
{
    protected override bool AreEqual(CircleShape a, CircleShape b, Tolerance t) =>
        a.Center.IsEqualTo(b.Center, t) && t.AreEqual(a.Radius, b.Radius);
}

/// <summary>Arcs are equal when center, radius, midpoint and both end points (in either order) match.</summary>
public sealed class ArcComparer : ShapeComparer<ArcShape>
{
    protected override bool AreEqual(ArcShape a, ArcShape b, Tolerance t)
    {
        if (!a.Center.IsEqualTo(b.Center, t) || !t.AreEqual(a.Radius, b.Radius)) return false;
        if (!a.MidPoint.IsEqualTo(b.MidPoint, t)) return false;

        var same = a.StartPoint.IsEqualTo(b.StartPoint, t) && a.EndPoint.IsEqualTo(b.EndPoint, t);
        var swapped = a.StartPoint.IsEqualTo(b.EndPoint, t) && a.EndPoint.IsEqualTo(b.StartPoint, t);
        return same || swapped;
    }
}

/// <summary>
/// Polylines are compared edge by edge:
/// - Open: same direction or reversed.
/// - Closed: the start vertex may also differ (rotated order).
/// Both sides are normalized first (see <see cref="PolylineShape.Normalize"/>).
/// LWPolylines, 2D and 3D polylines are read into the same model, so they are compared with each other too.
/// If the vertex counts differ after normalization (e.g. an extra intermediate vertex) they are not equal.
/// </summary>
public sealed class PolylineComparer : ShapeComparer<PolylineShape>
{
    protected override bool AreEqual(PolylineShape a, PolylineShape b, Tolerance t)
    {
        a = a.Normalize(t);
        b = b.Normalize(t);

        if (a.IsClosed != b.IsClosed || a.Vertices.Count != b.Vertices.Count) return false;

        if (a.Vertices.Count < 2)
            return a.Vertices.Count == 0 || a.Vertices[0].Position.IsEqualTo(b.Vertices[0].Position, t);

        var edgesA = a.GetEdges();
        var edgesB = b.GetEdges();
        var reversedB = edgesB.Reverse().Select(e => e.Reversed()).ToList();

        var shifts = a.IsClosed ? edgesA.Count : 1;
        for (var shift = 0; shift < shifts; shift++)
        {
            if (Matches(edgesA, edgesB, shift, t) || Matches(edgesA, reversedB, shift, t)) return true;
        }

        return false;
    }

    private static bool Matches(IReadOnlyList<PolylineEdge> a, IReadOnlyList<PolylineEdge> b, int shift, Tolerance t)
    {
        for (var i = 0; i < a.Count; i++)
        {
            if (!a[i].IsEqualTo(b[(i + shift) % b.Count], t)) return false;
        }
        return true;
    }
}

/// <summary>
/// Block references: same definition, position, rotation (a 360° difference counts as equal), scale, normal and
/// attribute values. Two blocks with different attributes (e.g. two door tags at the same spot) are not copies.
/// </summary>
public sealed class BlockComparer : ShapeComparer<BlockShape>
{
    protected override bool AreEqual(BlockShape a, BlockShape b, Tolerance t) =>
        string.Equals(a.DefinitionKey, b.DefinitionKey, StringComparison.OrdinalIgnoreCase)
        && a.Position.IsEqualTo(b.Position, t)
        && SameAngle(a.Rotation, b.Rotation, t.Value)
        && t.AreEqual(a.ScaleX, b.ScaleX)
        && t.AreEqual(a.ScaleY, b.ScaleY)
        && t.AreEqual(a.ScaleZ, b.ScaleZ)
        && a.Normal.IsEqualTo(b.Normal, t)
        && a.Attributes.SequenceEqual(b.Attributes, StringComparer.Ordinal);

    /// <summary>0 and 2π are the same angle.</summary>
    private static bool SameAngle(double a, double b, double tolerance)
    {
        var diff = Math.Abs(a - b) % (2 * Math.PI);
        return Math.Min(diff, 2 * Math.PI - diff) <= tolerance;
    }
}

/// <summary>Maps shape types to their comparers.</summary>
public sealed class ShapeComparerRegistry
{
    private readonly Dictionary<Type, IShapeComparer> _byType;

    /// <summary>Creates a registry from the given comparers (one per shape type).</summary>
    public ShapeComparerRegistry(IEnumerable<IShapeComparer> comparers)
    {
        _byType = comparers.ToDictionary(c => c.ShapeType);
    }

    /// <summary>Registry with comparers for Line, Circle, Arc, Polyline and Block.</summary>
    public static ShapeComparerRegistry Default { get; } = new(new IShapeComparer[]
    {
        new LineComparer(),
        new CircleComparer(),
        new ArcComparer(),
        new PolylineComparer(),
        new BlockComparer()
    });

    public bool TryGet(Type shapeType, [MaybeNullWhen(false)] out IShapeComparer comparer) =>
        _byType.TryGetValue(shapeType, out comparer);
}
