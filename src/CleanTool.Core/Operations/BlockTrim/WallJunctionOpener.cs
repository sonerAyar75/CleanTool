using CleanTool.Core.Geometry;

namespace CleanTool.Core.Operations.BlockTrim;

/// <summary>Result of <see cref="WallJunctionOpener.Open"/>.</summary>
/// <param name="Changed">
/// New geometry of every path that was cut, keyed by <see cref="WallPath.Id"/>: the remaining open chains, in
/// order (an empty list means nothing is left). Paths that were not cut are not in the dictionary.
/// </param>
/// <param name="CutCount">Number of wall bands a line was cut at.</param>
public sealed record WallJunctionResult(IReadOnlyDictionary<int, IReadOnlyList<IReadOnlyList<Point3>>> Changed, int CutCount);

/// <summary>
/// Opens T and cross (+) wall junctions: the parts of wall lines that run across another wall are removed, so
/// the junction looks like the faces of the rooms around it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Wall bands.</b> Two parallel, overlapping lines of the same group form a wall when their distance is at most
/// the max thickness and each one is the other's nearest parallel line (ties allowed: a face broken into
/// pieces pairs with all of them). The nearest-line rule keeps the gap between two neighbouring walls from being
/// taken as a wall. The band is the area between the two lines over the length where they overlap.
/// </para>
/// <para>
/// <b>Cuts.</b> A line that crosses a band from one face to the other and extends beyond both faces loses the part
/// between them, when it meets each face at that face's end (a T: the wall ends on this line; for an oblique wall
/// the two faces end at different positions) or at least one thickness away from both of its ends (a cross: the
/// wall continues on both sides). Lines that only reach a face
/// (wall end caps, door jambs, L corners) are never cut, and neither are overshoots shorter than the thickness.
/// </para>
/// All bands are found on the original geometry first, then every cut is applied, so the result does not depend
/// on the order of the lines. Geometry is evaluated in the XY plane.
/// </remarks>
public sealed class WallJunctionOpener
{
    private const double ParallelSine = 0.0045; // about 0.26 degrees

    private readonly Tolerance _tolerance;
    private readonly double _maxThickness;

    /// <summary>Creates an opener.</summary>
    /// <param name="tolerance">Distance within which points are equal.</param>
    /// <param name="maxThickness">Largest distance between two faces of one wall; must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxThickness"/> is not positive.</exception>
    public WallJunctionOpener(Tolerance tolerance, double maxThickness)
    {
        if (!(maxThickness > 0)) throw new ArgumentOutOfRangeException(nameof(maxThickness));
        _tolerance = tolerance;
        _maxThickness = maxThickness;
    }

    /// <summary>Finds the wall bands of every group and cuts the lines that cross them.</summary>
    /// <param name="paths">Straight open paths (lines and polylines without arcs).</param>
    /// <returns>The cut paths and the number of cuts.</returns>
    public WallJunctionResult Open(IEnumerable<WallPath> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var changed = new Dictionary<int, IReadOnlyList<IReadOnlyList<Point3>>>();
        var cuts = 0;

        foreach (var group in paths.GroupBy(p => p.Group, StringComparer.OrdinalIgnoreCase))
            cuts += OpenGroup(group.ToList(), changed);

        return new WallJunctionResult(changed, cuts);
    }

    private double Tol => _tolerance.Value;

    private int OpenGroup(List<WallPath> paths, Dictionary<int, IReadOnlyList<IReadOnlyList<Point3>>> changed)
    {
        var segments = new List<Seg>();
        foreach (var path in paths)
            for (var i = 0; i + 1 < path.Points.Count; i++)
            {
                var seg = Seg.Create(path.Id, i, path.Points[i], path.Points[i + 1]);
                if (seg.Length > Tol) segments.Add(seg);
            }

        var bands = FindBands(segments);
        if (bands.Count == 0) return 0;

        // Removed parameter intervals per segment.
        var removed = new Dictionary<int, List<(double From, double To)>>();
        var cutCount = 0;
        for (var s = 0; s < segments.Count; s++)
        foreach (var band in bands)
        {
            if (band.FaceA == s || band.FaceB == s) continue;
            if (!TryCut(segments[s], band, segments, out var from, out var to)) continue;

            if (!removed.TryGetValue(s, out var list)) removed[s] = list = new List<(double, double)>();
            list.Add((from, to));
            cutCount++;
        }

        if (cutCount == 0) return 0;

        foreach (var path in paths)
        {
            var own = Enumerable.Range(0, segments.Count).Where(s => segments[s].PathId == path.Id).ToList();
            if (!own.Any(removed.ContainsKey)) continue;
            changed[path.Id] = Rebuild(path, own.ToDictionary(s => segments[s].EdgeIndex, s => s), removed, segments);
        }

        return cutCount;
    }

    // ---------------------------------------------------------------- bands

    private List<Band> FindBands(List<Seg> segments)
    {
        var candidates = new List<(int I, int J, double Distance, double From, double To)>();
        var nearest = Enumerable.Repeat(double.MaxValue, segments.Count).ToArray();

        for (var i = 0; i < segments.Count; i++)
        for (var j = i + 1; j < segments.Count; j++)
        {
            var a = segments[i];
            var b = segments[j];
            if (Math.Abs(Cross(a.Ux, a.Uy, b.Ux, b.Uy)) > ParallelSine) continue;

            var distance = Math.Abs(Cross(a.Ux, a.Uy, b.Start.X - a.Start.X, b.Start.Y - a.Start.Y));
            if (distance <= Tol || distance > _maxThickness + Tol) continue;

            // Overlap along a's direction.
            var t1 = a.Along(b.Start);
            var t2 = a.Along(b.End);
            var from = Math.Max(0, Math.Min(t1, t2));
            var to = Math.Min(a.Length, Math.Max(t1, t2));
            if (to - from <= Tol) continue;

            candidates.Add((i, j, distance, from, to));
            nearest[i] = Math.Min(nearest[i], distance);
            nearest[j] = Math.Min(nearest[j], distance);
        }

        var bands = new List<Band>();
        foreach (var c in candidates)
        {
            if (c.Distance > nearest[c.I] + Tol || c.Distance > nearest[c.J] + Tol) continue;

            var a = segments[c.I];
            var b = segments[c.J];
            var side = Math.Sign(Cross(a.Ux, a.Uy, b.Start.X - a.Start.X, b.Start.Y - a.Start.Y));
            bands.Add(new Band(c.I, c.J, a.Start, a.Ux, a.Uy, -a.Uy * side, a.Ux * side, c.Distance, c.From, c.To));
        }

        return bands;
    }

    /// <summary>The parameter range of <paramref name="s"/> inside <paramref name="band"/>, if s crosses it as a junction.</summary>
    private bool TryCut(Seg s, Band band, List<Seg> segments, out double from, out double to)
    {
        from = to = 0;

        // Across coordinate of s: c(t) = c0 + t * dc; the faces are at c = 0 and c = thickness.
        var c0 = (s.Start.X - band.Origin.X) * band.Nx + (s.Start.Y - band.Origin.Y) * band.Ny;
        var dc = s.Ux * band.Nx + s.Uy * band.Ny;
        if (Math.Abs(dc) < ParallelSine) return false; // parallel to the wall

        var t0 = (0 - c0) / dc;
        var t1 = (band.Thickness - c0) / dc;
        from = Math.Min(t0, t1);
        to = Math.Max(t0, t1);

        // s must extend beyond both faces.
        if (from <= Tol || to >= s.Length - Tol) return false;

        // Where s meets each face: on the face's end (a T, also an oblique one where the two faces end at
        // different positions) or at least one thickness away from both ends (a cross). Anything else, such as an
        // overshoot at an L corner, is not a junction.
        return MeetsAsJunction(s, t0, segments[band.FaceA], band.Thickness)
            && MeetsAsJunction(s, t1, segments[band.FaceB], band.Thickness);
    }

    private bool MeetsAsJunction(Seg s, double t, Seg face, double thickness)
    {
        var x = s.Start.X + s.Ux * t;
        var y = s.Start.Y + s.Uy * t;
        var along = face.Along(new Point3(x, y, 0));

        if (along < -Tol || along > face.Length + Tol) return false;

        var atEnd = Math.Abs(along) <= Tol || Math.Abs(along - face.Length) <= Tol;
        var wellInside = along >= thickness - Tol && face.Length - along >= thickness - Tol;
        return atEnd || wellInside;
    }

    // ---------------------------------------------------------------- rebuilding paths

    private IReadOnlyList<IReadOnlyList<Point3>> Rebuild(
        WallPath path,
        Dictionary<int, int> segmentsByEdge,
        Dictionary<int, List<(double From, double To)>> removed,
        List<Seg> all)
    {
        var chains = new List<IReadOnlyList<Point3>>();
        var current = new List<Point3>();

        void Flush()
        {
            if (current.Count >= 2) chains.Add(current);
            current = new List<Point3>();
        }

        for (var e = 0; e + 1 < path.Points.Count; e++)
        {
            if (!segmentsByEdge.TryGetValue(e, out var index)) continue; // zero-length edge

            var seg = all[index];
            var cuts = removed.TryGetValue(index, out var list) ? Merge(list) : new List<(double, double)>();
            var kept = Keep(seg.Length, cuts);

            foreach (var (from, to) in kept)
            {
                var start = seg.At(from);
                var end = seg.At(to);

                if (from > Tol || current.Count == 0)
                {
                    Flush();
                    current.Add(from <= Tol ? path.Points[e] : start);
                }

                current.Add(to >= seg.Length - Tol ? path.Points[e + 1] : end);
                if (to < seg.Length - Tol) Flush();
            }

            if (kept.Count == 0) Flush();
        }

        Flush();
        return chains;
    }

    private static List<(double From, double To)> Merge(List<(double From, double To)> intervals)
    {
        var merged = new List<(double From, double To)>();
        foreach (var (from, to) in intervals.OrderBy(i => i.From))
        {
            if (merged.Count > 0 && from <= merged[^1].To) merged[^1] = (merged[^1].From, Math.Max(merged[^1].To, to));
            else merged.Add((from, to));
        }

        return merged;
    }

    private List<(double From, double To)> Keep(double length, List<(double From, double To)> cuts)
    {
        var kept = new List<(double, double)>();
        var position = 0.0;
        foreach (var (from, to) in cuts)
        {
            if (from - position > Tol) kept.Add((position, from));
            position = Math.Max(position, to);
        }

        if (length - position > Tol) kept.Add((position, length));
        return kept;
    }

    // ---------------------------------------------------------------- types

    private static double Cross(double ax, double ay, double bx, double by) => ax * by - ay * bx;

    private sealed record Seg(int PathId, int EdgeIndex, Point3 Start, Point3 End, double Ux, double Uy, double Length)
    {
        public static Seg Create(int pathId, int edgeIndex, Point3 start, Point3 end)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            return length == 0
                ? new Seg(pathId, edgeIndex, start, end, 0, 0, 0)
                : new Seg(pathId, edgeIndex, start, end, dx / length, dy / length, length);
        }

        public double Along(Point3 p) => (p.X - Start.X) * Ux + (p.Y - Start.Y) * Uy;

        public Point3 At(double t) => new(Start.X + Ux * t, Start.Y + Uy * t, Start.Z + (End.Z - Start.Z) * (Length == 0 ? 0 : t / Length));
    }

    /// <summary>A wall: faces A and B, origin on face A, along (Ux, Uy) from From to To, normal (Nx, Ny) towards B.</summary>
    private sealed record Band(
        int FaceA, int FaceB, Point3 Origin, double Ux, double Uy, double Nx, double Ny, double Thickness, double From, double To);
}
