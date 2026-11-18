using Autodesk.AutoCAD.DatabaseServices;
using CleanTool.AcadTool.Flattening;
using CleanTool.AcadTool.Infrastructure;
using CleanTool.Core.Model;
using CleanTool.Core.Operations.Flatten;

namespace CleanTool.AcadTool.Gateways;

/// <summary>AutoCAD implementation of IFlattenGateway. Works on the objects in the current scope.</summary>
internal sealed class AcadFlattenGateway : IFlattenGateway
{
    private readonly AcadSession _session;
    private readonly FlattenerRegistry _flatteners;

    /// <summary>Creates the gateway for one session.</summary>
    public AcadFlattenGateway(AcadSession session, FlattenerRegistry flatteners)
    {
        _session = session;
        _flatteners = flatteners;
    }

    /// <inheritdoc/>
    public IReadOnlyList<FlattenCandidate> GetCandidates()
    {
        var transaction = _session.Transaction;
        var candidates = new List<FlattenCandidate>();

        foreach (var id in _session.GetEntityIds())
        {
            if (id.IsErased) continue;
            if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;

            var range = _flatteners.Resolve(entity).GetZRange(entity, transaction);
            if (range is null) continue; // unbounded objects such as XLine/Ray

            candidates.Add(new FlattenCandidate(
                AcadSession.ToEntityHandle(id),
                entity.GetType().Name,
                range.Value.Min,
                range.Value.Max,
                _session.IsLayerLocked(entity.LayerId)));
        }

        return candidates;
    }

    /// <inheritdoc/>
    public bool TryFlatten(EntityHandle handle, out string? failureReason)
    {
        try
        {
            var transaction = _session.Transaction;
            var entity = (Entity)transaction.GetObject(_session.ToObjectId(handle), OpenMode.ForWrite);

            _flatteners.Resolve(entity).Flatten(entity, transaction);

            failureReason = null;
            return true;
        }
        catch (Exception ex)
        {
            // Autodesk.AutoCAD.Runtime.Exception also ends up here (e.g. eCannotScaleNonUniformly).
            failureReason = ex.Message;
            return false;
        }
    }
}
