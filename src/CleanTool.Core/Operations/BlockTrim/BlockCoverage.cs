using CleanTool.Core.Geometry;
using CleanTool.Core.Geometry.Shapes;
using CleanTool.Core.Model;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Operations.Layers;

namespace CleanTool.Core.Operations.BlockTrim;

/// <summary>
/// Finds the parts of loose objects that lie on blocks (used by the WallTrim tool).
/// Pure algorithm: it never touches the drawing, so it is fully unit testable.
///
/// For lines and the straight segments of polylines, the parts to remove are:
/// 1. Along edges: the part that overlaps straight block edges on the same line (block lines or polyline segments).
///    Applied for every block.
/// 2. Inside: the part that lies inside a closed outline (a closed straight-edged polyline or a circle).
///    Only for blocks whose name matches <see cref="BlockTrimOptions.TrimInsideBlocks"/> (e.g. columns).
///
/// If nothing is left the object is erased; if some parts remain it is replaced by those pieces.
/// Arcs and circles are never cut: they are erased only when the block contains the same arc or circle.
/// Only block content in the same space (Owner) is considered.
/// </summary>
public sealed class BlockCoverage
{
    private readonly Tolerance _tolerance;
    private readonly ComparedProperty _ignored;
    private readonly IReadOnlyList<string> _trimInsideBlocks;

    /// <summary>Creates the coverage finder.</summary>
    /// <param name="tolerance">Distance within which points coincide / lie on a line.</param>
    /// <param name="ignoredProperties">Properties ignored when matching a loose object against block content (All = geometry only).</param>
    /// <param name="trimInsideBlocks">Name patterns of blocks whose closed outlines trim lines passing through them.</param>
    public BlockCoverage(Tolerance tolerance, ComparedProperty ignoredProperties, IReadOnlyList<string>? trimInsideBlocks = null)
    {
        _tolerance = tolerance;
        _ignored = ignoredProperties;
        _trimInsideBlocks = trimInsideBlocks ?? Array.Empty<string>();
    }

    /// <summary>
    /// Informational notes from the last run (e.g. the "Trim inside" pattern matched no block; names of blocks
    /// with closed outlines in the selection). These are not erase/trim decisions.
    /// </summary>
    public IReadOnlyList<string> Notes => _notes;

    private readonly List<string> _notes = new();

    /// <summary>Decides, for every loose object, whether it is erased, trimmed or left alone.</summary>
    /// <param name="loose">Loose objects (candidates).</param>
    /// <param name="content">Block content in the coordinates of the spaces that hold the inserts.</param>
    /// <returns>Erase / replace / skip decisions. Objects that are not affected have no action.</returns>
    public IReadOnlyList<OverlapAction> FindCoveredLoose(IReadOnlyList<ShapeRecord> loose, IReadOnlyList<ShapeRecord> content)
    {
        var actions = new List<OverlapAction>();
        _notes.Clear();
        AddTrimInsideNotes(content);
        var indexes = content
            .GroupBy(c => c.Owner)
            .ToDictionary(g => g.Key, g => new ContentIndex(g, _tolerance, IsTrimInsideBlock));

        foreach (var item in loose)
        {
            if (!item.IsLoose || !indexes.TryGetValue(item.Owner, out var index)) continue;

            var label = $"{item.Shape.Kind} <{item.Handle}>";
            var cut = index.Cut(item.Shape, e => PropertyMatcher.Matches(item.Properties, e.Properties, _ignored));

            switch (cut.Outcome)
            {
                case CutOutcome.Removed:
                    actions.Add(item.IsOnLockedLayer
                        ? new OverlapAction(item.Handle, $"{label} is covered by block '{cut.BlockName}'", "it is on a locked layer")
                        : new OverlapAction(item.Handle, $"{label} is covered by block '{cut.BlockName}'", null));
                    continue;

                case CutOutcome.Trimmed:
                    actions.Add(item.IsOnLockedLayer
                        ? new OverlapAction(item.Handle, $"{label} crosses block '{cut.BlockName}'", "it is on a locked layer")
                        : new OverlapAction(item.Handle, $"{label} trimmed at block '{cut.BlockName}'", null, cut.Pieces));
                    continue;
            }

            // Diagnostics: the geometry lies on a block but the properties differ → tell the user why nothing happened.
            if (_ignored != ComparedProperty.All && index.Cut(item.Shape, _ => true) is { Outcome: not CutOutcome.Untouched } geometryOnly)
            {
                actions.Add(new OverlapAction(item.Handle,
                    $"{label} overlaps block '{geometryOnly.BlockName}'",
                    "its layer/color/linetype differs from the block (turn off 'Match properties')"));
            }
        }

        return actions;
    }

    /// <summary>
    /// When "Trim inside" is filled in wrongly (e.g. "true"), explains why instead of silently doing nothing.
    /// </summary>
    private void AddTrimInsideNotes(IReadOnlyList<ShapeRecord> content)
    {
        var outlineBlocks = content
            .Where(c => Region.IsOutline(c.Shape, _tolerance))
            .Select(c => c.BlockContent!.BlockName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (outlineBlocks.Count == 0) return;

        var names = string.Join(", ", outlineBlocks.Take(10)) + (outlineBlocks.Count > 10 ? ", ..." : "");

        if (_trimInsideBlocks.Count == 0)
        {
            _notes.Add($"Trim inside is empty: lines are only trimmed along block edges. Blocks with closed outlines in the selection: {names}.");
            return;
        }

        if (!outlineBlocks.Any(IsTrimInsideBlock))
        {
            _notes.Add($"Trim inside '{string.Join("; ", _trimInsideBlocks)}' matches no block with a closed outline. " +
                       $"Blocks with closed outlines in the selection: {names}.");
        }
    }

    private bool IsTrimInsideBlock(string blockName) =>
        _trimInsideBlocks.Any(pattern => WildcardMatcher.IsMatch(blockName, pattern));

    private enum CutOutcome { Untouched, Removed, Trimmed }

    private sealed record CutResult(CutOutcome Outcome, string? BlockName, IReadOnlyList<IShape>? Pieces)
    {
        /// <summary>Nothing was removed.</summary>
        public static CutResult Untouched { get; } = new(CutOutcome.Untouched, null, null);
    }

    /// <summary>Block content of one space: straight/arc edges, circles and regions for inside trimming.</summary>
    private sealed class ContentIndex
    {
        private readonly Tolerance _tolerance;
        private readonly List<ContentEdge> _edges = new();   // sorted by MinX
        private readonly List<(CircleShape Circle, ShapeRecord Source)> _circles = new();
        private readonly List<Region> _regions = new();

        /// <summary>Indexes the block content of one space.</summary>
        public ContentIndex(IEnumerable<ShapeRecord> content, Tolerance tolerance, Func<string, bool> trimsInside)
        {
            _tolerance = tolerance;

            foreach (var record in content)
            {
                var insideRegion = trimsInside(record.BlockContent!.BlockName);

                switch (record.Shape)
                {
                    case LineShape line:
                        _edges.Add(new ContentEdge(line.Segment.Start, line.Segment.End, 0, record));
                        break;

                    case PolylineShape polyline:
                        var normalized = polyline.Normalize(tolerance);
                        foreach (var edge in normalized.GetEdges())
                            _edges.Add(new ContentEdge(edge.Start, edge.End, edge.Bulge, record));

                        if (insideRegion && Region.TryFromPolygon(normalized, record) is { } polygon) _regions.Add(polygon);
                        break;

                    case ArcShape arc:
                        var arcEdge = ArcGeometry.ToEdge(arc);
                        _edges.Add(new ContentEdge(arcEdge.Start, arcEdge.End, arcEdge.Bulge, record));
                        break;

                    case CircleShape circle:
                        _circles.Add((circle, record));
                        if (insideRegion) _regions.Add(Region.FromCircle(circle, record));
                        break;
                }
            }

            _edges.Sort((a, b) => a.MinX.CompareTo(b.MinX));
        }

        /// <summary>Computes what remains of a loose shape after removing the parts that lie on blocks.</summary>
        public CutResult Cut(IShape shape, Func<ShapeRecord, bool> accepts) => shape switch
        {
            LineShape line => CutLine(line, accepts),
            PolylineShape polyline => CutPolyline(polyline, accepts),
            ArcShape arc => CoverArc(ArcGeometry.ToEdge(arc), accepts) is { } arcCover
                ? new CutResult(CutOutcome.Removed, arcCover.BlockContent!.BlockName, null)
                : CutResult.Untouched,
            CircleShape circle => _circles.FirstOrDefault(c =>
                    accepts(c.Source)
                    && c.Circle.Center.IsEqualTo(circle.Center, _tolerance)
                    && _tolerance.AreEqual(c.Circle.Radius, circle.Radius)).Source is { } circleCover
                ? new CutResult(CutOutcome.Removed, circleCover.BlockContent!.BlockName, null)
                : CutResult.Untouched,
            _ => CutResult.Untouched
        };

        private CutResult CutLine(LineShape line, Func<ShapeRecord, bool> accepts)
        {
            var a = line.Segment.Start;
            var b = line.Segment.End;
            var length = a.DistanceTo(b);
            if (length <= _tolerance.Value) return CutResult.Untouched;

            var (keep, blockName) = KeepIntervals(a, b, length, accepts);
            if (blockName is null) return CutResult.Untouched;
            if (keep.Count == 0) return new CutResult(CutOutcome.Removed, blockName, null);

            var u = Direction(a, b, length);
            var pieces = keep.Select(k => (IShape)new LineShape(new Segment3(At(a, u, k.From), At(a, u, k.To)))).ToList();
            return new CutResult(CutOutcome.Trimmed, blockName, pieces);
        }

        /// <summary>
        /// Processes a polyline segment by segment: straight segments are trimmed, arc segments are removed only when
        /// the block contains the same arc. Remaining parts are joined into one polyline piece as long as they are
        /// contiguous.
        /// </summary>
        private CutResult CutPolyline(PolylineShape polyline, Func<ShapeRecord, bool> accepts)
        {
            var normalized = polyline.Normalize(_tolerance);
            var edges = normalized.GetEdges();
            if (edges.Count == 0) return CutResult.Untouched;

            string? blockName = null;
            var kept = new List<(PolylineEdge Edge, List<(double From, double To)> Keep, double Length)>();

            foreach (var edge in edges)
            {
                var length = edge.Start.DistanceTo(edge.End);

                if (edge.Bulge != 0)
                {
                    var cover = CoverArc(edge, accepts);
                    if (cover is not null) blockName ??= cover.BlockContent!.BlockName;
                    kept.Add((edge, cover is null ? new() { (0, length) } : new(), length));
                    continue;
                }

                if (length <= _tolerance.Value)
                {
                    kept.Add((edge, new() { (0, length) }, length));
                    continue;
                }

                var (keep, name) = KeepIntervals(edge.Start, edge.End, length, accepts);
                blockName ??= name;
                kept.Add((edge, keep, length));
            }

            if (blockName is null) return CutResult.Untouched;
            if (kept.All(k => k.Keep.Count == 0)) return new CutResult(CutOutcome.Removed, blockName, null);

            var pieces = BuildPieces(kept, normalized.IsClosed);
            return new CutResult(CutOutcome.Trimmed, blockName, pieces);
        }

        private IReadOnlyList<IShape> BuildPieces(List<(PolylineEdge Edge, List<(double From, double To)> Keep, double Length)> kept, bool closed)
        {
            var tol = _tolerance.Value;
            var pieces = new List<List<PolylineVertex>>();
            List<PolylineVertex>? current = null;
            var startsAtFirstVertex = false;
            var endsAtLastVertex = false;

            for (var i = 0; i < kept.Count; i++)
            {
                var (edge, keep, length) = kept[i];
                var u = length > 0 ? Direction(edge.Start, edge.End, length) : new Point3(0, 0, 0);

                foreach (var (from, to) in keep)
                {
                    var start = edge.Bulge != 0 ? edge.Start : At(edge.Start, u, from);
                    var end = edge.Bulge != 0 ? edge.End : At(edge.Start, u, to);
                    var continues = current is not null && from <= tol && current[^1].Position.IsEqualTo(start, _tolerance);

                    if (continues)
                    {
                        current![^1] = current[^1] with { Bulge = edge.Bulge };
                    }
                    else
                    {
                        if (current is { Count: >= 2 }) pieces.Add(current);
                        current = new List<PolylineVertex> { new(start, edge.Bulge) };
                        if (i == 0 && from <= tol) startsAtFirstVertex = true;
                    }

                    current.Add(new PolylineVertex(end, 0));

                    if (to < length - tol)
                    {
                        pieces.Add(current);
                        current = null;
                    }
                    else if (i == kept.Count - 1)
                    {
                        endsAtLastVertex = true;
                    }
                }

                // A piece that does not reach the end of its segment was closed above; a segment with nothing kept also breaks the chain.
                if (keep.Count == 0 && current is not null)
                {
                    pieces.Add(current);
                    current = null;
                }
            }

            if (current is { Count: >= 2 }) pieces.Add(current);

            // In a closed polyline, if the first and last pieces meet at the start vertex, join them into one piece.
            if (closed && pieces.Count > 1 && startsAtFirstVertex && endsAtLastVertex)
            {
                var last = pieces[^1];
                var first = pieces[0];
                // The join point (the first vertex) becomes the last vertex of the last piece; carry over the first segment's bulge.
                last[^1] = last[^1] with { Bulge = first[0].Bulge };
                last.AddRange(first.Skip(1));
                pieces[0] = last;
                pieces.RemoveAt(pieces.Count - 1);
            }

            return pieces.Select(p => (IShape)new PolylineShape(p, IsClosed: false)).ToList();
        }

        /// <summary>
        /// What remains of the straight segment A-B (parameter range [0, length]) after removing the parts that lie
        /// on blocks. If blockName is null nothing was removed and the segment stays as it is.
        /// </summary>
        private (List<(double From, double To)> Keep, string? BlockName) KeepIntervals(Point3 a, Point3 b, double length, Func<ShapeRecord, bool> accepts)
        {
            var tol = _tolerance.Value;
            var u = Direction(a, b, length);
            var remove = new List<(double From, double To)>();
            string? blockName = null;

            // 1. Along edges: straight block edges on the same line.
            foreach (var edge in Candidates(a, b))
            {
                if (edge.Bulge != 0 || !accepts(edge.Source)) continue;
                if (!IsOnLine(edge.Start, a, u) || !IsOnLine(edge.End, a, u)) continue;

                var t1 = Dot(Sub(edge.Start, a), u);
                var t2 = Dot(Sub(edge.End, a), u);
                if (AddClipped(remove, Math.Min(t1, t2), Math.Max(t1, t2), length)) blockName ??= edge.Source.BlockContent!.BlockName;
            }

            // 2. Inside: intervals that lie inside closed outlines.
            foreach (var region in _regions)
            {
                if (!accepts(region.Source) || !region.MayIntersect(a, b, tol)) continue;

                foreach (var (from, to) in region.InsideIntervals(a, u, length, _tolerance))
                {
                    if (AddClipped(remove, from, to, length)) blockName ??= region.Source.BlockContent!.BlockName;
                }
            }

            if (blockName is null) return (new() { (0, length) }, null);

            // Merge the intervals to remove and take the complement.
            remove.Sort((x, y) => x.From.CompareTo(y.From));
            var keep = new List<(double From, double To)>();
            var cursor = 0.0;

            foreach (var (from, to) in remove)
            {
                if (from > cursor + tol) keep.Add((cursor, from));
                cursor = Math.Max(cursor, to);
            }

            if (cursor < length - tol) keep.Add((cursor, length));
            return (keep, blockName);
        }

        /// <summary>Clips [from, to] to [0, length] and adds it; returns true if a meaningful length remained.</summary>
        private bool AddClipped(List<(double From, double To)> intervals, double from, double to, double length)
        {
            from = Math.Max(0, from);
            to = Math.Min(length, to);
            if (to - from <= _tolerance.Value) return false;

            intervals.Add((from, to));
            return true;
        }

        /// <summary>Arc segment: the block must contain the same arc (in either direction).</summary>
        private ShapeRecord? CoverArc(PolylineEdge arc, Func<ShapeRecord, bool> accepts)
        {
            var reversed = arc.Reversed();
            foreach (var edge in Candidates(arc.Start, arc.End))
            {
                if (edge.Bulge == 0 || !accepts(edge.Source)) continue;

                var asEdge = new PolylineEdge(edge.Start, edge.End, edge.Bulge);
                if (asEdge.IsEqualTo(arc, _tolerance) || asEdge.IsEqualTo(reversed, _tolerance)) return edge.Source;
            }

            return null;
        }

        private bool IsOnLine(Point3 p, Point3 origin, Point3 direction)
        {
            var d = Sub(p, origin);
            var along = Dot(d, direction);
            var perpendicular = new Point3(d.X - along * direction.X, d.Y - along * direction.Y, d.Z - along * direction.Z);
            return Math.Sqrt(Dot(perpendicular, perpendicular)) <= _tolerance.Value;
        }

        /// <summary>Edges whose bounding box intersects the query (scan over the list sorted by MinX).</summary>
        private IEnumerable<ContentEdge> Candidates(Point3 a, Point3 b)
        {
            var tol = _tolerance.Value;
            double minX = Math.Min(a.X, b.X) - tol, maxX = Math.Max(a.X, b.X) + tol;
            double minY = Math.Min(a.Y, b.Y) - tol, maxY = Math.Max(a.Y, b.Y) + tol;
            double minZ = Math.Min(a.Z, b.Z) - tol, maxZ = Math.Max(a.Z, b.Z) + tol;

            foreach (var edge in _edges)
            {
                if (edge.MinX > maxX) yield break;
                if (edge.MaxX < minX || edge.MaxY < minY || edge.MinY > maxY || edge.MaxZ < minZ || edge.MinZ > maxZ) continue;
                yield return edge;
            }
        }
    }

    /// <summary>One edge of the block content. Its box is the box of the chord (end points), which is enough for equality and coverage tests.</summary>
    private sealed record ContentEdge(Point3 Start, Point3 End, double Bulge, ShapeRecord Source)
    {
        public double MinX { get; } = Math.Min(Start.X, End.X);
        public double MaxX { get; } = Math.Max(Start.X, End.X);
        public double MinY { get; } = Math.Min(Start.Y, End.Y);
        public double MaxY { get; } = Math.Max(Start.Y, End.Y);
        public double MinZ { get; } = Math.Min(Start.Z, End.Z);
        public double MaxZ { get; } = Math.Max(Start.Z, End.Z);
    }

    /// <summary>
    /// Region for inside trimming (in the XY plane): a closed straight-edged polygon or a circle.
    /// The line must be at the same elevation (Z) as the region.
    /// </summary>
    private sealed class Region
    {
        private readonly IReadOnlyList<Point3>? _polygon;
        private readonly CircleShape? _circle;
        private readonly double _z;
        private readonly double _minX, _minY, _maxX, _maxY;

        private Region(IReadOnlyList<Point3>? polygon, CircleShape? circle, ShapeRecord source)
        {
            _polygon = polygon;
            _circle = circle;
            Source = source;

            if (polygon is not null)
            {
                _z = polygon[0].Z;
                _minX = polygon.Min(p => p.X); _maxX = polygon.Max(p => p.X);
                _minY = polygon.Min(p => p.Y); _maxY = polygon.Max(p => p.Y);
            }
            else
            {
                var c = circle!;
                _z = c.Center.Z;
                _minX = c.Center.X - c.Radius; _maxX = c.Center.X + c.Radius;
                _minY = c.Center.Y - c.Radius; _maxY = c.Center.Y + c.Radius;
            }
        }

        /// <summary>The block content record that defines the region.</summary>
        public ShapeRecord Source { get; }

        /// <summary>Can the shape act as an inside-trimming outline? (closed straight-edged polyline or circle)</summary>
        public static bool IsOutline(IShape shape, Tolerance tolerance) => shape switch
        {
            CircleShape => true,
            PolylineShape polyline => IsPolygon(polyline.Normalize(tolerance)),
            _ => false
        };

        private static bool IsPolygon(PolylineShape polyline)
        {
            if (!polyline.IsClosed || polyline.Vertices.Count < 3) return false;
            if (polyline.Vertices.Any(v => v.Bulge != 0)) return false;

            var z = polyline.Vertices[0].Position.Z;
            return polyline.Vertices.All(v => Math.Abs(v.Position.Z - z) <= 1e-6);
        }

        /// <summary>A closed, planar (single Z) polyline with at least 3 vertices and no arcs becomes a region.</summary>
        public static Region? TryFromPolygon(PolylineShape polyline, ShapeRecord source) =>
            IsPolygon(polyline)
                ? new Region(polyline.Vertices.Select(v => v.Position).ToList(), null, source)
                : null;

        /// <summary>A circular region.</summary>
        public static Region FromCircle(CircleShape circle, ShapeRecord source) => new(null, circle, source);

        public bool MayIntersect(Point3 a, Point3 b, double tol) =>
            Math.Abs(a.Z - _z) <= tol && Math.Abs(b.Z - _z) <= tol
            && Math.Max(a.X, b.X) >= _minX - tol && Math.Min(a.X, b.X) <= _maxX + tol
            && Math.Max(a.Y, b.Y) >= _minY - tol && Math.Min(a.Y, b.Y) <= _maxY + tol;

        /// <summary>Intervals of the segment A + u·t (0 ≤ t ≤ length) that lie inside the region.</summary>
        public IEnumerable<(double From, double To)> InsideIntervals(Point3 a, Point3 u, double length, Tolerance tolerance)
        {
            return _circle is not null
                ? CircleIntervals(a, u, length, _circle)
                : PolygonIntervals(a, u, length, _polygon!, tolerance);
        }

        private static IEnumerable<(double, double)> CircleIntervals(Point3 a, Point3 u, double length, CircleShape circle)
        {
            // |A + u t - C|² = r²  (in the XY plane)
            var ux = u.X; var uy = u.Y;
            var uLenSq = ux * ux + uy * uy;
            if (uLenSq < 1e-18) yield break;

            var dx = a.X - circle.Center.X; var dy = a.Y - circle.Center.Y;
            var bq = 2 * (dx * ux + dy * uy);
            var cq = dx * dx + dy * dy - circle.Radius * circle.Radius;
            var disc = bq * bq - 4 * uLenSq * cq;
            if (disc <= 0) yield break;

            var root = Math.Sqrt(disc);
            var t1 = (-bq - root) / (2 * uLenSq);
            var t2 = (-bq + root) / (2 * uLenSq);
            if (t2 <= 0 || t1 >= length) yield break;

            yield return (t1, t2);
        }

        private static IEnumerable<(double, double)> PolygonIntervals(Point3 a, Point3 u, double length, IReadOnlyList<Point3> polygon, Tolerance tolerance)
        {
            // Intersections with the edges + the end points → is the midpoint of each consecutive interval inside?
            var cuts = new List<double> { 0, length };
            for (var i = 0; i < polygon.Count; i++)
            {
                var p = polygon[i];
                var q = polygon[(i + 1) % polygon.Count];
                if (TryIntersect(a, u, length, p, q, out var t)) cuts.Add(t);
            }

            cuts.Sort();
            for (var i = 0; i < cuts.Count - 1; i++)
            {
                var from = cuts[i];
                var to = cuts[i + 1];
                if (to - from <= tolerance.Value) continue;

                var mid = (from + to) / 2;
                var m = new Point3(a.X + u.X * mid, a.Y + u.Y * mid, a.Z);
                if (IsStrictlyInside(m, polygon, tolerance.Value)) yield return (from, to);
            }
        }

        /// <summary>XY intersection of A + u t with edge P-Q (parallel edges are skipped; overlap along edges is handled separately).</summary>
        private static bool TryIntersect(Point3 a, Point3 u, double length, Point3 p, Point3 q, out double t)
        {
            t = 0;
            var ex = q.X - p.X; var ey = q.Y - p.Y;
            var denom = u.X * ey - u.Y * ex;
            if (Math.Abs(denom) < 1e-12) return false;

            var wx = p.X - a.X; var wy = p.Y - a.Y;
            t = (wx * ey - wy * ex) / denom;            // distance along the segment
            var s = (wx * u.Y - wy * u.X) / denom;       // ratio along the edge [0,1]

            return t >= 0 && t <= length && s >= -1e-9 && s <= 1 + 1e-9;
        }

        /// <summary>Ray casting; a point on the boundary does not count as inside.</summary>
        private static bool IsStrictlyInside(Point3 m, IReadOnlyList<Point3> polygon, double tol)
        {
            var inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];

                if (DistanceToSegment(m, pj, pi) <= tol) return false;

                if ((pi.Y > m.Y) != (pj.Y > m.Y)
                    && m.X < (pj.X - pi.X) * (m.Y - pi.Y) / (pj.Y - pi.Y) + pi.X)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static double DistanceToSegment(Point3 m, Point3 p, Point3 q)
        {
            var ex = q.X - p.X; var ey = q.Y - p.Y;
            var lenSq = ex * ex + ey * ey;
            var t = lenSq == 0 ? 0 : Math.Clamp(((m.X - p.X) * ex + (m.Y - p.Y) * ey) / lenSq, 0, 1);
            var cx = p.X + t * ex - m.X; var cy = p.Y + t * ey - m.Y;
            return Math.Sqrt(cx * cx + cy * cy);
        }
    }

    private static Point3 Direction(Point3 a, Point3 b, double length) =>
        new((b.X - a.X) / length, (b.Y - a.Y) / length, (b.Z - a.Z) / length);

    private static Point3 At(Point3 origin, Point3 direction, double t) =>
        new(origin.X + direction.X * t, origin.Y + direction.Y * t, origin.Z + direction.Z * t);

    private static Point3 Sub(Point3 a, Point3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static double Dot(Point3 a, Point3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

/// <summary>Converts an arc into a polyline edge (start, end, bulge).</summary>
public static class ArcGeometry
{
    /// <summary>
    /// bulge = tan(sweep / 4); positive when the arc runs counter-clockwise (CCW) from start to end.
    /// The direction is found from the side the midpoint lies on (in the XY plane).
    /// </summary>
    public static PolylineEdge ToEdge(ArcShape arc)
    {
        var a1 = Angle(arc.Center, arc.StartPoint);
        var a2 = Angle(arc.Center, arc.EndPoint);
        var am = Angle(arc.Center, arc.MidPoint);

        var ccwSweep = Normalize(a2 - a1);
        var midIsOnCcwPath = Normalize(am - a1) < ccwSweep;

        var bulge = midIsOnCcwPath
            ? Math.Tan(ccwSweep / 4)
            : -Math.Tan((2 * Math.PI - ccwSweep) / 4);

        return new PolylineEdge(arc.StartPoint, arc.EndPoint, bulge);
    }

    private static double Angle(Point3 center, Point3 p) => Math.Atan2(p.Y - center.Y, p.X - center.X);

    private static double Normalize(double angle)
    {
        var twoPi = 2 * Math.PI;
        angle %= twoPi;
        return angle < 0 ? angle + twoPi : angle;
    }
}
