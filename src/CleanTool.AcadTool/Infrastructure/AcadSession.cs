using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CleanTool.Core.Geometry;
using CleanTool.Core.Model;

namespace CleanTool.AcadTool.Infrastructure;

/// <summary>
/// Access to the active document during one command run.
/// Gateways get the transaction from here; <see cref="AcadOperationScopeFactory"/> opens and closes it.
/// </summary>
internal sealed class AcadSession
{
    private readonly Dictionary<ObjectId, bool> _layerLockCache = new();

    /// <summary>Creates a session.</summary>
    /// <param name="document">The active document.</param>
    /// <param name="scope">Objects the gateways work on.</param>
    public AcadSession(Document document, IEntityScope scope)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    /// <summary>The objects the gateways work on (selection, whole space, block content, ...).</summary>
    public IEntityScope Scope { get; }

    /// <summary>The active document.</summary>
    public Document Document { get; }

    /// <summary>The document's database.</summary>
    public Database Database => Document.Database;

    /// <summary>The document's editor (command line).</summary>
    public Editor Editor => Document.Editor;

    /// <summary>The active transaction opened by the pipeline.</summary>
    public Transaction Transaction =>
        Document.TransactionManager.TopTransaction
        ?? throw new InvalidOperationException("No active transaction. Gateways must be used inside an operation scope.");

    /// <summary>Ids of the non-erased objects in scope.</summary>
    public IEnumerable<ObjectId> GetEntityIds() => Scope.GetEntityIds(Transaction, Database);

    public ObjectId ToObjectId(EntityHandle handle) =>
        Database.GetObjectId(false, new Handle(handle.Value), 0);

    /// <summary>Converts an ObjectId into Core's handle.</summary>
    public static EntityHandle ToEntityHandle(ObjectId id) => new(id.Handle.Value);

    /// <summary>Converts an AutoCAD point into a Core point.</summary>
    public static Point3 ToCore(Point3d p) => new(p.X, p.Y, p.Z);

    /// <summary>Is the layer locked? Results are cached for the session.</summary>
    public bool IsLayerLocked(ObjectId layerId)
    {
        if (_layerLockCache.TryGetValue(layerId, out var locked)) return locked;

        var layer = (LayerTableRecord)Transaction.GetObject(layerId, OpenMode.ForRead);
        locked = layer.IsLocked;
        _layerLockCache[layerId] = locked;
        return locked;
    }
}
