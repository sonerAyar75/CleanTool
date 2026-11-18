using CleanTool.Core.Abstractions;
using CleanTool.Core.Geometry;
using CleanTool.Core.Geometry.Shapes;
using CleanTool.Core.Model;
using CleanTool.Core.Operations.BlockTrim;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Operations.Flatten;
using CleanTool.Core.Operations.Layers;
using CleanTool.Core.Operations.Thickness;
using CleanTool.Core.Pipeline;

namespace CleanTool.Core.Tests.Fakes;

internal static class Make
{
    public static ShapeRecord Line(long handle, double x1, double y1, double x2, double y2,
        string layer = "A", bool locked = false, double z1 = 0, double z2 = 0) =>
        Record(handle, new LineShape(new Segment3(new Point3(x1, y1, z1), new Point3(x2, y2, z2))), layer, locked);

    public static ShapeRecord Circle(long handle, double cx, double cy, double radius,
        string layer = "A", bool locked = false, double cz = 0) =>
        Record(handle, new CircleShape(new Point3(cx, cy, cz), radius), layer, locked);

    /// <summary>Center (0,0), radius r; angles in degrees, counter-clockwise from start to end.</summary>
    public static ShapeRecord Arc(long handle, double radius, double startDeg, double endDeg,
        string layer = "A", bool reversed = false)
    {
        var start = OnCircle(radius, startDeg);
        var end = OnCircle(radius, endDeg);
        var mid = OnCircle(radius, (startDeg + endDeg) / 2);

        // reversed: the same arc with swapped ends, as if drawn with normal -Z.
        var shape = reversed
            ? new ArcShape(Point3.Origin, radius, end, start, mid)
            : new ArcShape(Point3.Origin, radius, start, end, mid);

        return Record(handle, shape, layer, false);
    }

    /// <summary>Vertices: (x, y, bulge).</summary>
    public static ShapeRecord Poly(long handle, bool closed, params (double X, double Y, double Bulge)[] vertices) =>
        PolyOn(handle, "A", closed, vertices);

    public static ShapeRecord PolyOn(long handle, string layer, bool closed, params (double X, double Y, double Bulge)[] vertices) =>
        PolyAtZ(handle, layer, closed, 0, vertices);

    public static ShapeRecord PolyAtZ(long handle, string layer, bool closed, double z, params (double X, double Y, double Bulge)[] vertices) =>
        Record(handle,
            new PolylineShape(vertices.Select(v => new PolylineVertex(new Point3(v.X, v.Y, z), v.Bulge)).ToList(), closed),
            layer, false);

    /// <summary>Default space (e.g. model space). Block tests pass a different owner.</summary>
    public const long DefaultOwner = 1;

    public static ShapeRecord Record(long handle, IShape shape, string layer = "A", bool locked = false, long owner = DefaultOwner) =>
        new(new EntityHandle(handle), new EntityHandle(owner), shape, DisplayProperties.OnLayer(layer), locked);

    /// <summary>Changes the display properties of a record (for color, linetype etc. tests).</summary>
    public static ShapeRecord With(this ShapeRecord record, Func<DisplayProperties, DisplayProperties> change) =>
        record with { Properties = change(record.Properties) };

    public static ShapeRecord InOwner(this ShapeRecord record, long owner) =>
        record with { Owner = new EntityHandle(owner) };

    /// <summary>Marks the record as block content (if cannotRemove is null it may be erased from the definition).</summary>
    public static ShapeRecord InBlock(this ShapeRecord record, string blockName = "DOOR", long insert = 900, string? cannotRemove = null) =>
        record with { BlockContent = new BlockContentInfo(blockName, new EntityHandle(insert), cannotRemove) };

    public static FlattenCandidate Candidate(long handle, double minZ, double maxZ, bool locked = false) =>
        new(new EntityHandle(handle), "Line", minZ, maxZ, locked);

    public static LayerRecord Layer(string name, bool inUse = false, bool current = false, bool xref = false) =>
        new(name, inUse, current, xref);

    private static Point3 OnCircle(double r, double deg)
    {
        var rad = deg * Math.PI / 180;
        return new Point3(r * Math.Cos(rad), r * Math.Sin(rad), 0);
    }
}

internal sealed class FakeFlattenGateway : IFlattenGateway
{
    private readonly List<FlattenCandidate> _candidates;

    public FakeFlattenGateway(params FlattenCandidate[] candidates) => _candidates = candidates.ToList();

    public HashSet<EntityHandle> FailOn { get; } = new();
    public List<EntityHandle> Flattened { get; } = new();

    public IReadOnlyList<FlattenCandidate> GetCandidates() => _candidates;

    public bool TryFlatten(EntityHandle handle, out string? failureReason)
    {
        if (FailOn.Contains(handle))
        {
            failureReason = "boom";
            return false;
        }

        Flattened.Add(handle);
        failureReason = null;
        return true;
    }
}

internal sealed class FakeThicknessGateway : IThicknessGateway
{
    private readonly List<ThicknessCandidate> _candidates;

    public FakeThicknessGateway(params ThicknessCandidate[] candidates) => _candidates = candidates.ToList();

    public List<EntityHandle> Reset { get; } = new();

    public IReadOnlyList<ThicknessCandidate> GetCandidates() => _candidates;

    public bool TryResetThickness(EntityHandle handle, out string? failureReason)
    {
        Reset.Add(handle);
        failureReason = null;
        return true;
    }
}

internal sealed class FakeDuplicateGateway : IDuplicateGateway, IBlockTrimGateway
{
    private readonly List<ShapeRecord> _shapes;

    public FakeDuplicateGateway(params ShapeRecord[] shapes) => _shapes = shapes.ToList();

    public List<EntityHandle> Erased { get; } = new();

    public IReadOnlyList<ShapeRecord> GetShapes() => _shapes;

    /// <summary>Block content (already in world coordinates) — filled in by the test.</summary>
    public List<ShapeRecord> BlockContent { get; } = new();

    public int BlockContentRequests { get; private set; }

    /// <summary>Trimmed objects and the pieces that replaced them.</summary>
    public Dictionary<EntityHandle, IReadOnlyList<IShape>> Replaced { get; } = new();

    public bool TryReplace(EntityHandle handle, IReadOnlyList<IShape> pieces, out string? failureReason)
    {
        Replaced[handle] = pieces;
        failureReason = null;
        return true;
    }

    /// <summary>Closed polylines added by the outline step: property source and vertices.</summary>
    public List<(EntityHandle Source, IReadOnlyList<Point3> Vertices)> AddedPolylines { get; } = new();

    /// <summary>When true, TryAddClosedPolyline fails (to test that source lines are then kept).</summary>
    public bool FailPolylines { get; set; }

    public bool TryAddClosedPolyline(EntityHandle propertySource, IReadOnlyList<Point3> vertices, out string? failureReason)
    {
        if (FailPolylines)
        {
            failureReason = "test failure";
            return false;
        }

        AddedPolylines.Add((propertySource, vertices));
        failureReason = null;
        return true;
    }

    public IReadOnlyList<ShapeRecord> GetBlockContentShapes()
    {
        BlockContentRequests++;
        return BlockContent;
    }

    public bool TryErase(EntityHandle handle, out string? failureReason)
    {
        Erased.Add(handle);
        failureReason = null;
        return true;
    }
}

internal sealed class FakeLayerGateway : ILayerGateway
{
    private readonly List<LayerRecord> _layers;

    public FakeLayerGateway(params LayerRecord[] layers) => _layers = layers.ToList();

    public List<string> Deleted { get; } = new();

    public IReadOnlyList<LayerRecord> GetLayers() => _layers;

    public bool TryDelete(string layerName, out string? failureReason)
    {
        Deleted.Add(layerName);
        failureReason = null;
        return true;
    }
}

/// <summary>Records whether each scope was completed.</summary>
internal sealed class RecordingScopeFactory : IOperationScopeFactory
{
    public List<(string OperationId, bool Completed)> Scopes { get; } = new();

    public IOperationScope Begin(string operationId) => new Scope(this, operationId);

    private sealed class Scope : IOperationScope
    {
        private readonly RecordingScopeFactory _owner;
        private readonly string _id;
        private bool _completed;

        public Scope(RecordingScopeFactory owner, string id)
        {
            _owner = owner;
            _id = id;
        }

        public void Complete() => _completed = true;

        public void Dispose() => _owner.Scopes.Add((_id, _completed));
    }
}

internal sealed class StubOperation : ICleanOperation
{
    private readonly Func<CleanContext, OperationResult>? _execute;
    private readonly List<string>? _callLog;

    public StubOperation(string id, List<string>? callLog = null, Func<CleanContext, OperationResult>? execute = null,
        CleanStage stage = CleanStage.Flatten)
    {
        Id = id;
        Stage = stage;
        _callLog = callLog;
        _execute = execute;
    }

    public string Id { get; }
    public string DisplayName => Id;
    public CleanStage Stage { get; }

    public OperationResult Execute(CleanContext context)
    {
        _callLog?.Add(Id);
        if (_execute is not null) return _execute(context);

        var b = new OperationResultBuilder(Id, DisplayName);
        b.AddExamined();
        b.AddChanged();
        return b.Build();
    }
}

internal sealed class ListLogger : ICleanLogger
{
    public List<string> Lines { get; } = new();
    public void Info(string message) => Lines.Add(message);
    public void Warn(string message) => Lines.Add("WARN " + message);
}
