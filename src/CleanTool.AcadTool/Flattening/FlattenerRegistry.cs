using Autodesk.AutoCAD.DatabaseServices;

namespace CleanTool.AcadTool.Flattening;

/// <summary>
/// Chooses the flattener for an object. Specific flatteners are tried in order; the fallback is used if none applies.
/// </summary>
internal sealed class FlattenerRegistry
{
    private readonly IReadOnlyList<IEntityFlattener> _specific;
    private readonly IEntityFlattener _fallback;

    /// <summary>Creates a registry.</summary>
    /// <param name="specific">Flatteners tried in order.</param>
    /// <param name="fallback">Used when no specific flattener applies.</param>
    public FlattenerRegistry(IEnumerable<IEntityFlattener> specific, IEntityFlattener fallback)
    {
        _specific = specific.ToList();
        _fallback = fallback;
    }

    /// <summary>The default flatteners: block reference, line, LWPolyline, 3D polyline, spline, and the generic fallback.</summary>
    public static FlattenerRegistry CreateDefault(double tolerance) => new(
        new IEntityFlattener[]
        {
            new BlockReferenceFlattener(),
            new LineFlattener(),
            new LwPolylineFlattener(),
            new Polyline3dFlattener(),
            new SplineFlattener()
        },
        new GenericFlattener(tolerance));

    public IEntityFlattener Resolve(Entity entity) =>
        _specific.FirstOrDefault(f => f.CanHandle(entity)) ?? _fallback;
}
