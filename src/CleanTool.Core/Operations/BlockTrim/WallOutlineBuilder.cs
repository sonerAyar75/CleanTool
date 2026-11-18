using CleanTool.Core.Geometry;

namespace CleanTool.Core.Operations.BlockTrim;

/// <summary>
/// An open chain of straight edges (a line or an open polyline without arcs) that may be part of a wall face.
/// </summary>
/// <param name="Id">Caller's id; returned in <see cref="WallOutline.SourceIds"/> and <see cref="WallOutlineResult.UsedPaths"/>.</param>
/// <param name="Group">Only paths with the same group are joined (the layer; a selection is always in one space).</param>
/// <param name="Points">Vertices in order; at least two.</param>
public sealed record WallPath(int Id, string Group, IReadOnlyList<Point3> Points);

/// <summary>A closed outline built from wall paths and end caps.</summary>
/// <param name="Group">Group of the paths it was built from.</param>
/// <param name="Vertices">Vertices counter-clockwise, first vertex not repeated, collinear vertices removed.</param>
/// <param name="SourceIds">Ids of the paths that contributed at least one edge.</param>
/// <param name="CapCount">Number of end caps (closing edges between two wall faces) in the outline.</param>
public sealed record WallOutline(string Group, IReadOnlyList<Point3> Vertices, IReadOnlyList<int> SourceIds, int CapCount);

/// <summary>Result of <see cref="WallOutlineBuilder.Build"/>.</summary>
/// <param name="Outlines">Closed outlines found.</param>
/// <param name="UsedPaths">Ids of the paths whose every edge is part of an outline (they can be replaced by it).</param>
/// <param name="UnclosedPaths">Ids of the paths that are part of no outline at all.</param>
/// <param name="Notes">Messages for the report (ambiguous caps, open chains).</param>
public sealed record WallOutlineResult(
    IReadOnlyList<WallOutline> Outlines,
    IReadOnlySet<int> UsedPaths,
    IReadOnlySet<int> UnclosedPaths,
    IReadOnlyList<string> Notes);

/// <summary>
/// Turns wall lines (two parallel faces per wall) into closed outlines, in four steps:
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><description>
/// <b>End caps.</b> A free end (an end point no other edge of the group touches) is closed with the free end of a
/// parallel face that runs in the same direction from its end. The distance between the two faces is the wall
/// thickness; it must be at most <see cref="WallOutlineBuilder.MaxThickness"/>. Each end is matched with its
/// shortest candidate. When an end has two equally good candidates nothing is guessed: both ends stay open and
/// the case is reported. This also closes a wall that runs into an untrimmed wall face (a T junction): the cap
/// lies on the face it runs into and the two walls become separate outlines. Ends that are offset along the
/// wall (an oblique T) are accepted up to twice the thickness.
/// </description></item>
/// <item><description>
/// <b>Corners.</b> Before oblique caps are considered, two free ends of non-parallel lines whose lines intersect
/// within the max thickness of both ends (a small gap or overshoot at an L corner) are extended or trimmed to the
/// intersection, like FILLET with radius 0. Caps at aligned ends (a wall trimmed at a column face) come first, so a
/// wall ending at a column is never extended into it.
/// </description></item>
/// <item><description>
/// <b>Joining.</b> Edges and caps of a group that share end points are joined, like PEDIT Join. A face that is
/// broken where another wall meets it (a trimmed T junction) simply continues into that wall's faces, so the
/// whole junction becomes one outline. Chains that do not close are left alone and reported.
/// </description></item>
/// <item><description>
/// <b>Faces.</b> The closed loops are traced with the "next edge clockwise" rule, which also works where more
/// than two edges meet at a point. Every bounded loop becomes an outline. A loop inside another one (the inner
/// face of a wall ring without openings) is returned as its own outline; a hatch treats it as an island.
/// </description></item>
/// </list>
/// Geometry is evaluated in the XY plane; Z of the first point of each node is kept.
/// </remarks>
public sealed class WallOutlineBuilder
{
    /// <summary>How far an end cap may be offset along the wall, relative to the wall thickness.</summary>
    public const double MaxCapSkew = 2.0;

    private const double ParallelCosine = 0.99999; // about 0.26 degrees
    private const double MinCornerSine = 0.0872;   // corners need at least 5 degrees between the lines

    private readonly Tolerance _tolerance;

    /// <summary>Creates a builder.</summary>
    /// <param name="tolerance">Distance within which end points are equal.</param>
    /// <param name="maxThickness">Largest distance between two faces of one wall (drawing units); must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxThickness"/> is not positive.</exception>
    public WallOutlineBuilder(Tolerance tolerance, double maxThickness)
    {
        if (!(maxThickness > 0)) throw new ArgumentOutOfRangeException(nameof(maxThickness));
        _tolerance = tolerance;
        MaxThickness = maxThickness;
    }

    /// <summary>Largest distance between two faces of one wall.</summary>
    public double MaxThickness { get; }

    /// <summary>Builds the closed outlines of all groups.</summary>
    /// <param name="paths">Wall paths; paths with fewer than two distinct points are ignored.</param>
    /// <returns>Outlines, used and unclosed path ids, and report notes.</returns>
    public WallOutlineResult Build(IEnumerable<WallPath> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var outlines = new List<WallOutline>();
        var used = new HashSet<int>();
        var unclosed = new HashSet<int>();
        var notes = new List<string>();

        var groups = new List<GroupBuilder>();
        foreach (var group in paths.GroupBy(p => p.Group, StringComparer.OrdinalIgnoreCase))
        {
            var builder = new GroupBuilder(this, group.Key, group.ToList());
            builder.Build(outlines, used, unclosed, notes);
            groups.Add(builder);
        }

        notes.AddRange(LayerMeetings(groups));
        return new WallOutlineResult(outlines, used, unclosed, notes);
    }

    /// <summary>
    /// Reports places where a free line end of one group touches a line end of another group: usually wall lines
    /// drawn on the wrong layer, which are therefore not joined.
    /// </summary>
    private IEnumerable<string> LayerMeetings(List<GroupBuilder> groups)
    {
        var reported = new HashSet<(string, string)>(EqualityComparer<(string, string)>.Default);
        for (var i = 0; i < groups.Count; i++)
        for (var j = 0; j < groups.Count; j++)
        {
            if (i == j) continue;
            var a = groups[i];
            var b = groups[j];
            var key = string.CompareOrdinal(a.Name, b.Name) < 0 ? (a.Name, b.Name) : (b.Name, a.Name);
            if (reported.Contains(key)) continue;

            foreach (var end in a.InitialFreeEnds)
            {
                if (!b.Points.Any(p => Distance2D(p, end) <= _tolerance.Value)) continue;
                reported.Add(key);
                yield return $"Lines on layers '{a.Name}' and '{b.Name}' meet at ({end.X:0.###}, {end.Y:0.###}); " +
                             "lines on different layers are not joined (move them to the same layer).";
                break;
            }
        }
    }

    // ---------------------------------------------------------------- one group

    private sealed class GroupBuilder
    {
        private readonly WallOutlineBuilder _owner;
        private readonly string _group;
        private readonly List<WallPath> _paths;
        private readonly List<Point3> _nodes = new();
        private readonly Dictionary<(long, long), List<int>> _grid = new();
        private readonly List<Edge> _edges = new();
        private readonly Dictionary<(int, int), int> _edgeByNodes = new();

        public GroupBuilder(WallOutlineBuilder owner, string group, List<WallPath> paths)
        {
            _owner = owner;
            _group = group;
            _paths = paths;
        }

        private double Tol => _owner._tolerance.Value;

        /// <summary>Layer (group) name.</summary>
        public string Name => _group;

        /// <summary>Line ends that no other line of this group touched, before any cap or corner join.</summary>
        public List<Point3> InitialFreeEnds { get; private set; } = new();

        /// <summary>All end points and vertices of this group.</summary>
        public IReadOnlyList<Point3> Points => _nodes;

        public void Build(List<WallOutline> outlines, HashSet<int> used, HashSet<int> unclosed, List<string> notes)
        {
            foreach (var path in _paths) AddPath(path);
            if (_edges.Count == 0) return;
            InitialFreeEnds = FreeEnds().Select(e => _nodes[e.Node]).ToList();

            AddCaps(notes, aligned: true);   // ends trimmed at a column face
            JoinCorners();                   // L corners whose lines do not quite meet
            RebuildEdgeIndex();
            AddCaps(notes, aligned: false);  // oblique ends (a wall meeting another at an angle)

            var alive = PruneOpenChains();
            var faces = TraceFaces(alive, notes);

            var edgeInFace = new bool[_edges.Count];
            foreach (var face in faces)
            {
                foreach (var e in face.Edges) edgeInFace[e] = true;

                var sources = face.Edges.SelectMany(e => _edges[e].Sources).Distinct().OrderBy(i => i).ToList();
                var caps = face.Edges.Count(e => _edges[e].IsCap);
                outlines.Add(new WallOutline(_group, Simplify(face.Nodes), sources, caps));
            }

            // A path is "used" when every edge of it is in some outline, "unclosed" when none is.
            var edgesOfPath = new Dictionary<int, List<int>>();
            for (var e = 0; e < _edges.Count; e++)
                foreach (var source in _edges[e].Sources)
                {
                    if (!edgesOfPath.TryGetValue(source, out var list)) edgesOfPath[source] = list = new List<int>();
                    list.Add(e);
                }

            var openCount = 0;
            foreach (var path in _paths)
            {
                if (!edgesOfPath.TryGetValue(path.Id, out var list)) continue;
                if (list.All(e => edgeInFace[e])) used.Add(path.Id);
                else if (list.All(e => !edgeInFace[e])) { unclosed.Add(path.Id); openCount++; }
            }

            if (openCount > 0)
                notes.Add($"Layer '{_group}': {openCount} wall line(s) are not part of a closed outline (open chain or no parallel face within the max thickness).");
        }

        // ------------------------------------------------------------ graph

        private void AddPath(WallPath path)
        {
            int? previous = null;
            foreach (var point in path.Points)
            {
                var node = NodeAt(point);
                if (previous is int p && p != node) AddEdge(p, node, path.Id, isCap: false);
                previous = node;
            }
        }

        private int NodeAt(Point3 point)
        {
            var size = Math.Max(Tol, 1e-12);
            var cx = (long)Math.Floor(point.X / size);
            var cy = (long)Math.Floor(point.Y / size);

            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if (!_grid.TryGetValue((cx + dx, cy + dy), out var cell)) continue;
                foreach (var index in cell)
                    if (Distance2D(_nodes[index], point) <= Tol) return index;
            }

            _nodes.Add(point);
            var key = (cx, cy);
            if (!_grid.TryGetValue(key, out var list)) _grid[key] = list = new List<int>();
            list.Add(_nodes.Count - 1);
            return _nodes.Count - 1;
        }

        private void AddEdge(int a, int b, int source, bool isCap)
        {
            var key = a < b ? (a, b) : (b, a);
            if (_edgeByNodes.TryGetValue(key, out var existing))
            {
                // Duplicate edge (overlapping copies): keep one edge, remember every source.
                if (source >= 0 && !_edges[existing].Sources.Contains(source)) _edges[existing].Sources.Add(source);
                return;
            }

            _edgeByNodes[key] = _edges.Count;
            _edges.Add(new Edge(a, b, isCap, source >= 0 ? new List<int> { source } : new List<int>()));
        }

        private List<int>[] Adjacency(IReadOnlyList<bool>? alive = null)
        {
            var adjacency = new List<int>[_nodes.Count];
            for (var i = 0; i < adjacency.Length; i++) adjacency[i] = new List<int>();
            for (var e = 0; e < _edges.Count; e++)
            {
                if (alive is not null && !alive[e]) continue;
                adjacency[_edges[e].A].Add(e);
                adjacency[_edges[e].B].Add(e);
            }

            return adjacency;
        }

        // ------------------------------------------------------------ step 1: end caps

        private List<FreeEnd> FreeEnds()
        {
            var adjacency = Adjacency();
            var ends = new List<FreeEnd>();
            for (var n = 0; n < _nodes.Count; n++)
            {
                if (adjacency[n].Count != 1) continue;
                var edgeIndex = adjacency[n][0];
                var other = _edges[edgeIndex].Other(n);
                var dx = _nodes[other].X - _nodes[n].X;
                var dy = _nodes[other].Y - _nodes[n].Y;
                var inward = Unit(dx, dy);
                ends.Add(new FreeEnd(n, inward.X, inward.Y, edgeIndex, Math.Sqrt(dx * dx + dy * dy)));
            }

            return ends;
        }

        private void AddCaps(List<string> notes, bool aligned)
        {
            var ends = FreeEnds();

            var candidates = new List<(int I, int J, double Length)>();
            for (var i = 0; i < ends.Count; i++)
            for (var j = i + 1; j < ends.Count; j++)
                if (TryCap(ends[i], ends[j], aligned, out var length)) candidates.Add((i, j, length));

            var byEnd = new Dictionary<int, List<(int Other, double Length)>>();
            foreach (var c in candidates)
            {
                Add(byEnd, c.I, (c.J, c.Length));
                Add(byEnd, c.J, (c.I, c.Length));
            }

            var taken = new bool[ends.Count];
            var ambiguous = new bool[ends.Count];
            foreach (var c in candidates.OrderBy(c => c.Length))
            {
                if (taken[c.I] || taken[c.J] || ambiguous[c.I] || ambiguous[c.J]) continue;

                if (HasRival(byEnd[c.I], c.J, c.Length, taken) || HasRival(byEnd[c.J], c.I, c.Length, taken))
                {
                    ambiguous[c.I] = ambiguous[c.J] = true;
                    var p = _nodes[ends[c.I].Node];
                    notes.Add($"Layer '{_group}': the wall end at ({p.X:0.###}, {p.Y:0.###}) has more than one matching face; it was left open.");
                    continue;
                }

                taken[c.I] = taken[c.J] = true;
                AddEdge(ends[c.I].Node, ends[c.J].Node, -1, isCap: true);
            }
        }

        private bool HasRival(List<(int Other, double Length)> options, int partner, double length, bool[] taken) =>
            options.Any(o => o.Other != partner && !taken[o.Other] && Math.Abs(o.Length - length) <= Tol);

        private bool TryCap(FreeEnd a, FreeEnd b, bool aligned, out double length)
        {
            length = 0;

            // Parallel and running the same way from their ends.
            var cos = a.Dx * b.Dx + a.Dy * b.Dy;
            if (cos < ParallelCosine) return false;

            var pa = _nodes[a.Node];
            var pb = _nodes[b.Node];
            var vx = pb.X - pa.X;
            var vy = pb.Y - pa.Y;

            var along = Math.Abs(vx * a.Dx + vy * a.Dy);
            var across = Math.Abs(vx * a.Dy - vy * a.Dx); // wall thickness

            if (across <= Tol || across > _owner.MaxThickness + Tol) return false;
            if (along > across * MaxCapSkew + Tol) return false;
            if (aligned != (along <= Tol)) return false;

            length = Math.Sqrt(vx * vx + vy * vy);
            return true;
        }

        // ------------------------------------------------------------ corner joins (FILLET radius 0)

        /// <summary>
        /// Two free ends of non-parallel lines whose lines intersect close to both ends (a small gap or overshoot at an
        /// L corner) are moved to the intersection point and become one node, so the faces chain around the corner.
        /// "Close" means within the max wall thickness; an overshoot may not consume the whole line.
        /// </summary>
        private void JoinCorners()
        {
            var ends = FreeEnds();
            var candidates = new List<(int I, int J, double Score, double X, double Y)>();

            for (var i = 0; i < ends.Count; i++)
            for (var j = i + 1; j < ends.Count; j++)
            {
                var a = ends[i];
                var b = ends[j];
                var cross = a.Dx * b.Dy - a.Dy * b.Dx;
                if (Math.Abs(cross) < MinCornerSine) continue; // parallel: that is a cap, not a corner

                var pa = _nodes[a.Node];
                var pb = _nodes[b.Node];
                var wx = pb.X - pa.X;
                var wy = pb.Y - pa.Y;

                // pa + s * da = pb + u * db; positive s/u = the corner lies inside the line (overshoot).
                var s = (wx * b.Dy - wy * b.Dx) / cross;
                var u = (wx * a.Dy - wy * a.Dx) / cross;

                var reach = _owner.MaxThickness + Tol;
                if (Math.Abs(s) > reach || Math.Abs(u) > reach) continue;
                if (s >= a.Length - Tol || u >= b.Length - Tol) continue;

                candidates.Add((i, j, Math.Abs(s) + Math.Abs(u), pa.X + a.Dx * s, pa.Y + a.Dy * s));
            }

            var taken = new bool[ends.Count];
            foreach (var c in candidates.OrderBy(c => c.Score))
            {
                if (taken[c.I] || taken[c.J]) continue;
                taken[c.I] = taken[c.J] = true;

                var keep = ends[c.I].Node;
                var drop = ends[c.J].Node;
                _nodes[keep] = _nodes[keep] with { X = c.X, Y = c.Y };

                var edge = _edges[ends[c.J].Edge];
                if (edge.A == drop) edge.A = keep; else edge.B = keep;
            }
        }

        private void RebuildEdgeIndex()
        {
            _edgeByNodes.Clear();
            for (var e = 0; e < _edges.Count; e++)
            {
                var (a, b) = (_edges[e].A, _edges[e].B);
                _edgeByNodes[a < b ? (a, b) : (b, a)] = e;
            }
        }

        // ------------------------------------------------------------ step 2: drop open chains

        private bool[] PruneOpenChains()
        {
            var alive = Enumerable.Repeat(true, _edges.Count).ToArray();
            var degree = new int[_nodes.Count];
            foreach (var e in _edges) { degree[e.A]++; degree[e.B]++; }

            var adjacency = Adjacency();
            var queue = new Queue<int>(Enumerable.Range(0, _nodes.Count).Where(n => degree[n] == 1));
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                if (degree[n] != 1) continue;
                foreach (var e in adjacency[n])
                {
                    if (!alive[e]) continue;
                    alive[e] = false;
                    degree[n]--;
                    var other = _edges[e].Other(n);
                    if (--degree[other] == 1) queue.Enqueue(other);
                }
            }

            return alive;
        }

        // ------------------------------------------------------------ step 3: faces

        private List<Face> TraceFaces(bool[] alive, List<string> notes)
        {
            var adjacency = Adjacency(alive);

            // Outgoing half-edges of every node sorted counter-clockwise by angle.
            var sorted = new List<(int Edge, int To, double Angle)>[_nodes.Count];
            for (var n = 0; n < _nodes.Count; n++)
                sorted[n] = adjacency[n]
                    .Select(e => (Edge: e, To: _edges[e].Other(n)))
                    .Select(t => (t.Edge, t.To, Angle: Math.Atan2(_nodes[t.To].Y - _nodes[n].Y, _nodes[t.To].X - _nodes[n].X)))
                    .OrderBy(t => t.Angle)
                    .ToList();

            var visited = new HashSet<(int From, int To)>();
            var faces = new List<Face>();

            for (var e = 0; e < _edges.Count; e++)
            {
                if (!alive[e]) continue;
                foreach (var (first, second) in new[] { (_edges[e].A, _edges[e].B), (_edges[e].B, _edges[e].A) })
                {
                    if (visited.Contains((first, second))) continue;

                    var nodes = new List<int>();
                    var edges = new List<int>();
                    var (u, v, edge) = (first, second, e);
                    var guard = 0;
                    while (visited.Add((u, v)) && guard++ <= 2 * _edges.Count)
                    {
                        nodes.Add(u);
                        edges.Add(edge);
                        (edge, var w) = NextClockwise(sorted[v], u);
                        (u, v) = (v, w);
                    }

                    if (nodes.Count < 3 || SignedArea(nodes) <= Tol * Tol) continue; // outer (clockwise) loop

                    if (edges.Distinct().Count() != edges.Count)
                    {
                        var p = _nodes[nodes[0]];
                        notes.Add($"Layer '{_group}': an outline near ({p.X:0.###}, {p.Y:0.###}) touches itself; it was not created.");
                        continue;
                    }

                    faces.Add(new Face(nodes, edges));
                }
            }

            return faces;
        }

        /// <summary>At node v, coming from u: the next outgoing edge clockwise after v→u.</summary>
        private static (int Edge, int To) NextClockwise(List<(int Edge, int To, double Angle)> around, int cameFrom)
        {
            var index = around.FindIndex(t => t.To == cameFrom);
            var next = around[(index - 1 + around.Count) % around.Count];
            return (next.Edge, next.To);
        }

        private double SignedArea(List<int> nodes)
        {
            double sum = 0;
            for (var i = 0; i < nodes.Count; i++)
            {
                var a = _nodes[nodes[i]];
                var b = _nodes[nodes[(i + 1) % nodes.Count]];
                sum += a.X * b.Y - b.X * a.Y;
            }

            return sum / 2;
        }

        private IReadOnlyList<Point3> Simplify(List<int> nodes)
        {
            var points = nodes.Select(n => _nodes[n]).ToList();
            var changed = true;
            while (changed && points.Count > 3)
            {
                changed = false;
                for (var i = 0; i < points.Count && points.Count > 3; i++)
                {
                    var prev = points[(i - 1 + points.Count) % points.Count];
                    var cur = points[i];
                    var next = points[(i + 1) % points.Count];
                    if (!IsStraightThrough(prev, cur, next)) continue;
                    points.RemoveAt(i);
                    changed = true;
                    i--;
                }
            }

            return points;
        }

        private bool IsStraightThrough(Point3 a, Point3 b, Point3 c)
        {
            var abx = b.X - a.X; var aby = b.Y - a.Y;
            var bcx = c.X - b.X; var bcy = c.Y - b.Y;
            var length = Math.Sqrt(abx * abx + aby * aby) + Math.Sqrt(bcx * bcx + bcy * bcy);
            if (length <= Tol) return true;

            // Distance of b from the line a-c, and b must lie between a and c.
            var acx = c.X - a.X; var acy = c.Y - a.Y;
            var ac = Math.Sqrt(acx * acx + acy * acy);
            if (ac <= Tol) return false;
            var offLine = Math.Abs(abx * acy - aby * acx) / ac;
            return offLine <= Tol && abx * bcx + aby * bcy > 0;
        }

        private static void Add(Dictionary<int, List<(int, double)>> map, int key, (int, double) value)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<(int, double)>();
            list.Add(value);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static double Distance2D(Point3 a, Point3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static (double X, double Y) Unit(double x, double y)
    {
        var length = Math.Sqrt(x * x + y * y);
        return length == 0 ? (0, 0) : (x / length, y / length);
    }

    private sealed class Edge
    {
        public Edge(int a, int b, bool isCap, List<int> sources)
        {
            A = a;
            B = b;
            IsCap = isCap;
            Sources = sources;
        }

        public int A { get; set; }
        public int B { get; set; }
        public bool IsCap { get; }
        public List<int> Sources { get; }

        public int Other(int node) => node == A ? B : A;
    }

    /// <summary>A node touched by one edge only; (Dx, Dy) points from the end into its line.</summary>
    private readonly record struct FreeEnd(int Node, double Dx, double Dy, int Edge, double Length);

    private sealed record Face(List<int> Nodes, List<int> Edges);
}
