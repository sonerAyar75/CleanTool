using Autodesk.AutoCAD.DatabaseServices;
using CleanTool.AcadTool.Infrastructure;
using CleanTool.Core.Operations.Layers;

namespace CleanTool.AcadTool.Gateways;

/// <summary>
/// AutoCAD implementation of ILayerGateway.
/// "Is the layer in use?" is answered by Database.Purge: the most reliable way, including references in block definitions.
/// </summary>
internal sealed class AcadLayerGateway : ILayerGateway
{
    private readonly AcadSession _session;
    private readonly Dictionary<string, ObjectId> _idsByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the gateway for one session.</summary>
    public AcadLayerGateway(AcadSession session)
    {
        _session = session;
    }

    /// <inheritdoc/>
    public IReadOnlyList<LayerRecord> GetLayers()
    {
        var transaction = _session.Transaction;
        var database = _session.Database;
        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);

        var allIds = new ObjectIdCollection();
        foreach (ObjectId id in layerTable)
        {
            if (!id.IsErased) allIds.Add(id);
        }

        // Purge filters the collection in place: only purgeable (unused) ids remain.
        var purgeable = new ObjectIdCollection();
        foreach (ObjectId id in allIds) purgeable.Add(id);
        database.Purge(purgeable);
        var purgeableSet = new HashSet<ObjectId>(purgeable.Cast<ObjectId>());

        _idsByName.Clear();
        var records = new List<LayerRecord>(allIds.Count);

        foreach (ObjectId id in allIds)
        {
            var layer = (LayerTableRecord)transaction.GetObject(id, OpenMode.ForRead);
            _idsByName[layer.Name] = id;

            records.Add(new LayerRecord(
                layer.Name,
                IsInUse: !purgeableSet.Contains(id),
                IsCurrent: id == database.Clayer,
                IsXrefDependent: layer.IsDependent));
        }

        return records;
    }

    /// <inheritdoc/>
    public bool TryDelete(string layerName, out string? failureReason)
    {
        if (!_idsByName.TryGetValue(layerName, out var id))
        {
            failureReason = "Layer not found. Call GetLayers first.";
            return false;
        }

        try
        {
            var layer = (LayerTableRecord)_session.Transaction.GetObject(id, OpenMode.ForWrite);
            layer.Erase();

            failureReason = null;
            return true;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            return false;
        }
    }
}
