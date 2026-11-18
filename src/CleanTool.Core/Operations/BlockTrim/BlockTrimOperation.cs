using CleanTool.Core.Abstractions;
using CleanTool.Core.Geometry;
using CleanTool.Core.Geometry.Shapes;
using CleanTool.Core.Model;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Operations.Layers;
using CleanTool.Core.Pipeline;

namespace CleanTool.Core.Operations.BlockTrim;

/// <summary>Settings of the WallTrim tool (<see cref="BlockTrimOperation"/>).</summary>
public sealed record BlockTrimOptions
{
    /// <summary>Distance within which points are equal / lie on a line (drawing units).</summary>
    public Tolerance Tolerance { get; init; } = new(1e-4);

    /// <summary>
    /// Name patterns (* and ?) of blocks whose closed outlines (closed straight polylines, circles) cut the lines that
    /// pass THROUGH them, e.g. "K*", "KOLON*". When empty, lines are trimmed only along block edges. This is
    /// deliberately opt-in so that large closed blocks such as title-block frames do not remove everything inside them.
    /// </summary>
    public IReadOnlyList<string> TrimInsideBlocks { get; init; } = Array.Empty<string>();

    /// <summary>
    /// false (default): only geometry counts (block content on layer 0 → A-COLUMN still cuts a wall line on A-WALL).
    /// true: layer, color, linetype etc. must also be the same.
    /// </summary>
    public bool MatchProperties { get; init; }

    /// <summary>
    /// After trimming, join the remaining wall lines into closed polylines (see <see cref="WallOutlineBuilder"/>):
    /// the free ends of two parallel faces are closed and lines that meet end to end on the same layer are joined.
    /// </summary>
    public bool CreateClosedOutlines { get; init; }

    /// <summary>Largest distance between the two faces of one wall (drawing units). Used by <see cref="CreateClosedOutlines"/>.</summary>
    public double MaxWallThickness { get; init; } = 60;

    /// <summary>
    /// false (default): lines that became part of a closed polyline are erased (like PEDIT Join).
    /// true: they are kept and the closed polylines are added on top.
    /// </summary>
    public bool KeepOriginalLines { get; init; }

    /// <summary>
    /// Open T and cross (+) wall junctions (see <see cref="WallJunctionOpener"/>): the parts of wall lines that run
    /// across another wall are removed. Runs after the trim at blocks and before <see cref="CreateClosedOutlines"/>,
    /// so an opened T junction becomes one closed polyline. Uses <see cref="MaxWallThickness"/>.
    /// </summary>
    public bool OpenJunctions { get; init; }

    /// <summary>
    /// Layer name patterns (* and ?) of the wall lines used by <see cref="OpenJunctions"/> and
    /// <see cref="CreateClosedOutlines"/>, e.g. "DUVAR*; *WALL*". Empty = every layer. Lines on other layers (windows,
    /// furniture) are never cut or joined. The trim at blocks is not affected.
    /// </summary>
    public IReadOnlyList<string> WallLayers { get; init; } = Array.Empty<string>();
}

/// <summary>Port through which the WallTrim operation reads and changes the drawing.</summary>
public interface IBlockTrimGateway
{
    /// <summary>Loose objects in scope (candidates for trimming).</summary>
    IReadOnlyList<ShapeRecord> GetShapes();

    /// <summary>Content of the block references in scope, in the coordinates of the space that holds each insert.</summary>
    IReadOnlyList<ShapeRecord> GetBlockContentShapes();

    bool TryErase(EntityHandle handle, out string? failureReason);

    /// <summary>
    /// Replaces the object with the given pieces. The pieces are added to the same space with the original's
    /// properties and the original is erased. Pieces can be <see cref="LineShape"/> or open <see cref="PolylineShape"/>.
    /// </summary>
    bool TryReplace(EntityHandle handle, IReadOnlyList<IShape> pieces, out string? failureReason);

    /// <summary>
    /// Adds a closed polyline with the given vertices to the space that holds <paramref name="propertySource"/>,
    /// with that object's properties (layer, color, linetype, ...).
    /// </summary>
    bool TryAddClosedPolyline(EntityHandle propertySource, IReadOnlyList<Point3> vertices, out string? failureReason);
}

/// <summary>
/// Cuts loose lines that run over or through blocks (e.g. columns) at the block face:
/// parts overlapping block edges are removed for every block, parts inside closed outlines only for the blocks in
/// <see cref="BlockTrimOptions.TrimInsideBlocks"/>. Objects with nothing left are erased.
/// The decisions are made by <see cref="BlockCoverage"/>; this class only applies them.
/// </summary>
public sealed class BlockTrimOperation : ICleanOperation
{
    /// <summary>Stable operation id used by commands and settings.</summary>
    public const string OperationId = "block-trim";

    private readonly IBlockTrimGateway _gateway;
    private readonly BlockTrimOptions _options;

    /// <summary>Creates the operation.</summary>
    /// <param name="gateway">Access to the drawing.</param>
    /// <param name="options">Settings; defaults are used when null.</param>
    public BlockTrimOperation(IBlockTrimGateway gateway, BlockTrimOptions? options = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _options = options ?? new BlockTrimOptions();
    }

    /// <inheritdoc/>
    public string Id => OperationId;

    /// <inheritdoc/>
    public string DisplayName => "Trim at blocks";

    /// <inheritdoc/>
    public CleanStage Stage => CleanStage.Deduplicate;

    /// <inheritdoc/>
    public OperationResult Execute(CleanContext context)
    {
        var result = new OperationResultBuilder(Id, DisplayName);

        var loose = _gateway.GetShapes().Where(s => s.IsLoose).ToList();
        result.AddExamined(loose.Count);
        if (loose.Count == 0) return result.Build();

        var actions = FindTrimActions(loose, result);
        if (actions is null) return result.Build();

        if (_options.CreateClosedOutlines || _options.OpenJunctions)
        {
            new WallEditStep(this, loose, actions, context.DryRun, result).Run();
            return result.Build();
        }

        foreach (var action in actions)
        {
            if (action.SkipReason is not null)
            {
                result.AddSkipped($"{action.Label}: not changed, {action.SkipReason}.");
                continue;
            }

            if (context.DryRun)
            {
                result.AddChanged();
                if (action.ShouldReplace) result.AddMessage($"{action.Label} ({action.Pieces!.Count} piece(s) would remain).");
                continue;
            }

            Apply(action, result);
        }

        return result.Build();
    }

    /// <summary>Trim decisions; null when there is nothing to do at all.</summary>
    private IReadOnlyList<OverlapAction>? FindTrimActions(List<ShapeRecord> loose, OperationResultBuilder result)
    {
        var content = _gateway.GetBlockContentShapes();
        if (content.Count == 0)
        {
            if (!_options.CreateClosedOutlines && !_options.OpenJunctions)
            {
                result.AddMessage("No blocks in the selection: select the columns (blocks) together with the wall lines.");
                return null;
            }

            result.AddMessage("No blocks in the selection: lines are not trimmed at columns.");
            return Array.Empty<OverlapAction>();
        }

        var ignored = _options.MatchProperties ? ComparedProperty.None : ComparedProperty.All;
        var coverage = new BlockCoverage(_options.Tolerance, ignored, _options.TrimInsideBlocks);
        var actions = coverage.FindCoveredLoose(loose, content);
        foreach (var note in coverage.Notes) result.AddMessage(note);
        return actions;
    }

    private void Apply(OverlapAction action, OperationResultBuilder result)
    {
        string? reason;
        var ok = action.ShouldReplace
            ? _gateway.TryReplace(action.Handle, action.Pieces!, out reason)
            : _gateway.TryErase(action.Handle, out reason);

        if (!ok)
        {
            result.AddFailed($"{action.Label}: {reason ?? "unknown error"}");
            return;
        }

        result.AddChanged();
        if (action.ShouldReplace) result.AddMessage($"{action.Label} ({action.Pieces!.Count} piece(s) remain).");
    }

    /// <summary>
    /// Trim at blocks, open junctions and closed polylines, computed in memory in that order, then applied once.
    /// The preview therefore shows the real outcome, and a line that is cut and then joined into a polyline is
    /// edited once instead of three times.
    /// </summary>
    private sealed class WallEditStep
    {
        private readonly BlockTrimOperation _op;
        private readonly List<ShapeRecord> _loose;
        private readonly Dictionary<EntityHandle, OverlapAction> _actions;
        private readonly bool _dryRun;
        private readonly OperationResultBuilder _result;

        // Per loose object: its trim action and its current pieces.
        private readonly List<Item> _items = new();

        public WallEditStep(BlockTrimOperation op, List<ShapeRecord> loose, IReadOnlyList<OverlapAction> actions, bool dryRun, OperationResultBuilder result)
        {
            _op = op;
            _loose = loose;
            _actions = actions.GroupBy(a => a.Handle).ToDictionary(g => g.Key, g => g.First());
            _dryRun = dryRun;
            _result = result;
        }

        private BlockTrimOptions Options => _op._options;

        public void Run()
        {
            // 1. Trim at blocks (in memory).
            foreach (var record in _loose)
            {
                _actions.TryGetValue(record.Handle, out var action);
                if (action?.SkipReason is not null)
                    _result.AddSkipped($"{action.Label}: not changed, {action.SkipReason}.");

                IReadOnlyList<IShape> pieces = action is null || action.SkipReason is not null ? new[] { record.Shape }
                    : action.ShouldReplace ? action.Pieces! : Array.Empty<IShape>();

                var usable = !record.IsOnLockedLayer && action?.SkipReason is null;
                _items.Add(new Item(record, action, pieces.ToList(), usable));
            }

            // 2. Open T and cross junctions.
            var junctionCuts = 0;
            var opened = 0;
            if (Options.OpenJunctions)
            {
                var (paths, owners) = Paths();
                var junctions = new WallJunctionOpener(Options.Tolerance, Options.MaxWallThickness).Open(paths);
                junctionCuts = junctions.CutCount;

                // Replace each cut piece by its remaining chains (pieces are processed from the back so indexes stay valid).
                foreach (var (id, chains) in junctions.Changed.OrderByDescending(c => owners[c.Key].Piece))
                {
                    var (r, p) = owners[id];
                    var item = _items[r];
                    var original = item.Pieces[p];
                    item.Pieces.RemoveAt(p);
                    item.Pieces.InsertRange(p, chains.Select(c => ToShape(original, c)));
                    if (!item.Opened) opened++;
                    item.Opened = true;
                }
            }

            // 3. Closed polylines.
            WallOutlineResult? built = null;
            var consumed = new HashSet<(int Record, int Piece)>();
            Dictionary<int, (int Record, int Piece)>? outlineOwners = null;
            if (Options.CreateClosedOutlines)
            {
                (var paths, outlineOwners) = Paths();
                built = new WallOutlineBuilder(Options.Tolerance, Options.MaxWallThickness).Build(paths);
                foreach (var note in built.Notes) _result.AddMessage(note);
                if (!Options.KeepOriginalLines)
                    foreach (var id in built.UsedPaths) consumed.Add(outlineOwners[id]);
            }

            if (_dryRun)
            {
                Preview(opened, junctionCuts, built, consumed);
                return;
            }

            if (junctionCuts > 0) _result.AddMessage($"{opened} wall line(s) opened at {junctionCuts} junction crossing(s).");

            // 4. Polylines first: they take their properties from a source object that is erased afterwards.
            if (built is not null)
            {
                var created = 0;
                foreach (var outline in built.Outlines)
                {
                    var source = _items[outlineOwners![outline.SourceIds[0]].Record].Record.Handle;
                    if (_op._gateway.TryAddClosedPolyline(source, outline.Vertices, out var reason))
                    {
                        created++;
                        continue;
                    }

                    _result.AddFailed($"Closed polyline ({outline.Vertices.Count} vertices): {reason ?? "unknown error"}");
                    foreach (var id in outline.SourceIds) consumed.Remove(outlineOwners[id]); // never erase lines whose polyline is missing
                }

                _result.AddChanged(created);
                if (created > 0) _result.AddMessage($"{created} closed polyline(s) created.");
            }

            // 5. Then the line edits (trim, junctions, erase consumed lines).
            for (var r = 0; r < _items.Count; r++)
            {
                var item = _items[r];
                if (item.Action?.SkipReason is not null) continue;

                var left = item.Pieces.Where((_, p) => !consumed.Contains((r, p))).ToList();
                var untouched = item.Action is null && !item.Opened && left.Count == item.Pieces.Count;
                if (untouched) continue;

                var label = item.Action?.Label ?? $"{item.Record.Shape.Kind} {item.Record.Handle}";
                string? reason;
                var ok = left.Count == 0
                    ? _op._gateway.TryErase(item.Record.Handle, out reason)
                    : _op._gateway.TryReplace(item.Record.Handle, left, out reason);

                if (ok) _result.AddChanged();
                else _result.AddFailed($"{label}: {reason ?? "unknown error"}");
            }
        }

        /// <summary>Wall paths of the current pieces: straight lines and open polylines without arcs, usable objects only.</summary>
        private (List<WallPath> Paths, Dictionary<int, (int Record, int Piece)> Owners) Paths()
        {
            var paths = new List<WallPath>();
            var owners = new Dictionary<int, (int Record, int Piece)>();
            for (var r = 0; r < _items.Count; r++)
            {
                var item = _items[r];
                if (!item.Usable || !IsWallLayer(item.Record.Properties.Layer)) continue;
                for (var p = 0; p < item.Pieces.Count; p++)
                {
                    var points = StraightPoints(item.Pieces[p]);
                    if (points is null) continue;
                    owners[paths.Count] = (r, p);
                    paths.Add(new WallPath(paths.Count, item.Record.Properties.Layer, points));
                }
            }

            return (paths, owners);
        }

        private bool IsWallLayer(string layer) =>
            Options.WallLayers.Count == 0 || Options.WallLayers.Any(pattern => WildcardMatcher.IsMatch(layer, pattern));

        private void Preview(int opened, int junctionCuts, WallOutlineResult? built, HashSet<(int Record, int Piece)> consumed)
        {
            var trimmed = _items.Count(i => i.Action is not null && i.Action.SkipReason is null);
            _result.AddChanged(trimmed + opened + (built?.Outlines.Count ?? 0));

            if (trimmed > 0) _result.AddMessage($"{trimmed} object(s) would be trimmed at blocks.");
            if (Options.OpenJunctions)
                _result.AddMessage($"{opened} wall line(s) would be opened at {junctionCuts} junction crossing(s).");
            if (built is not null)
                _result.AddMessage($"{built.Outlines.Count} closed polyline(s) would be created"
                    + (Options.KeepOriginalLines ? " (original lines kept)." : $"; {consumed.Count} wall line piece(s) would be joined into them."));
        }

        /// <summary>A remaining chain as the same kind of shape as the piece it came from.</summary>
        private static IShape ToShape(IShape original, IReadOnlyList<Point3> chain) =>
            original is LineShape || chain.Count == 2 && original is not PolylineShape
                ? new LineShape(new Segment3(chain[0], chain[^1]))
                : new PolylineShape(chain.Select(p => new PolylineVertex(p, 0)).ToList(), false);

        /// <summary>Vertices of a straight open shape; null for arcs, circles, blocks and closed polylines.</summary>
        private static IReadOnlyList<Point3>? StraightPoints(IShape shape) => shape switch
        {
            LineShape line => new[] { line.Segment.Start, line.Segment.End },
            PolylineShape { IsClosed: false } poly when poly.Vertices.Count >= 2 && poly.GetEdges().All(e => e.Bulge == 0)
                => poly.Vertices.Select(v => v.Position).ToList(),
            _ => null,
        };

        private sealed class Item
        {
            public Item(ShapeRecord record, OverlapAction? action, List<IShape> pieces, bool usable)
            {
                Record = record;
                Action = action;
                Pieces = pieces;
                Usable = usable;
            }

            public ShapeRecord Record { get; }
            public OverlapAction? Action { get; }
            public List<IShape> Pieces { get; }
            public bool Usable { get; }
            public bool Opened { get; set; }
        }
    }
}
