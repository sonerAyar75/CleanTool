using Autodesk.AutoCAD.DatabaseServices;
using CleanTool.AcadTool.Infrastructure;
using CleanTool.Core.Model;
using CleanTool.Core.Operations.Thickness;

namespace CleanTool.AcadTool.Gateways;

/// <summary>
/// In AutoCAD, Thickness is not on a common base class but a separate property on each type.
/// This class collects the supported types in one place. A new type = one line in each of the two switches.
/// </summary>
internal static class ThicknessAccessor
{
    /// <summary>Reads the thickness of a supported entity; returns false for types without thickness.</summary>
    public static bool TryGet(Entity entity, out double thickness)
    {
        double? value = entity switch
        {
            Line e => e.Thickness,
            Circle e => e.Thickness,
            Arc e => e.Thickness,
            Polyline e => e.Thickness,
            Polyline2d e => e.Thickness,
            DBText e => e.Thickness,          // AttributeReference / AttributeDefinition are DBText too
            Solid e => e.Thickness,           // 2D solid
            Trace e => e.Thickness,
            DBPoint e => e.Thickness,
            Shape e => e.Thickness,
            _ => null
        };

        thickness = value ?? 0;
        return value.HasValue;
    }

    /// <summary>Sets the thickness of a supported entity to zero.</summary>
    public static void Reset(Entity entity)
    {
        switch (entity)
        {
            case Line e: e.Thickness = 0; break;
            case Circle e: e.Thickness = 0; break;
            case Arc e: e.Thickness = 0; break;
            case Polyline e: e.Thickness = 0; break;
            case Polyline2d e: e.Thickness = 0; break;
            case DBText e: e.Thickness = 0; break;
            case Solid e: e.Thickness = 0; break;
            case Trace e: e.Thickness = 0; break;
            case DBPoint e: e.Thickness = 0; break;
            case Shape e: e.Thickness = 0; break;
            default: throw new InvalidOperationException($"{entity.GetType().Name} has no thickness property.");
        }
    }
}

/// <summary>AutoCAD implementation of IThicknessGateway. Also covers the attributes of block references.</summary>
internal sealed class AcadThicknessGateway : IThicknessGateway
{
    private readonly AcadSession _session;

    /// <summary>Creates the gateway for one session.</summary>
    public AcadThicknessGateway(AcadSession session)
    {
        _session = session;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ThicknessCandidate> GetCandidates()
    {
        var transaction = _session.Transaction;
        var candidates = new List<ThicknessCandidate>();

        foreach (var id in _session.GetEntityIds())
        {
            if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;

            Add(entity, candidates);

            // Attributes are owned by the block reference; they do not show up when scanning a space.
            if (entity is BlockReference block)
            {
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    if (!attributeId.IsErased && transaction.GetObject(attributeId, OpenMode.ForRead) is Entity attribute)
                        Add(attribute, candidates);
                }
            }
        }

        return candidates;
    }

    /// <inheritdoc/>
    public bool TryResetThickness(EntityHandle handle, out string? failureReason)
    {
        try
        {
            var entity = (Entity)_session.Transaction.GetObject(_session.ToObjectId(handle), OpenMode.ForWrite);
            ThicknessAccessor.Reset(entity);

            failureReason = null;
            return true;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            return false;
        }
    }

    private void Add(Entity entity, List<ThicknessCandidate> candidates)
    {
        if (!ThicknessAccessor.TryGet(entity, out var thickness)) return;

        candidates.Add(new ThicknessCandidate(
            AcadSession.ToEntityHandle(entity.ObjectId),
            entity.GetType().Name,
            thickness,
            _session.IsLayerLocked(entity.LayerId)));
    }
}
