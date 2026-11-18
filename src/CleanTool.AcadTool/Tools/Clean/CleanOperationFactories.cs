using CleanTool.AcadTool.Composition;
using CleanTool.AcadTool.Flattening;
using CleanTool.AcadTool.Gateways;
using CleanTool.AcadTool.Infrastructure;
using CleanTool.AcadTool.ShapeReading;
using CleanTool.AcadTool.UI;
using CleanTool.Core.Abstractions;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Operations.Flatten;
using CleanTool.Core.Operations.Layers;
using CleanTool.Core.Operations.Thickness;

namespace CleanTool.AcadTool.Tools.Clean;

// Operations of the Clean tool: each factory connects a Core operation with its AutoCAD gateway
// and provides the name, description and options page shown in the dialog.

/// <summary>Thickness = 0 (<see cref="ThicknessOperation"/>).</summary>
internal sealed class ThicknessOperationFactory : IOperationFactory
{
    /// <inheritdoc/>
    public string Id => ThicknessOperation.OperationId;

    /// <inheritdoc/>
    public string DisplayName => "Thickness = 0";

    /// <inheritdoc/>
    public string Description => "Removes the extrusion (thickness) of lines, circles, arcs, polylines, texts and attributes. Runs before Flatten.";

    /// <inheritdoc/>
    public string CommandKeyword => "Thickness";

    /// <inheritdoc/>
    public bool UsesSelection => true;

    public ICleanOperation Create(AcadSession session, ToolSettings settings) =>
        new ThicknessOperation(new AcadThicknessGateway(session), settings.Thickness);

    /// <inheritdoc/>
    public IOptionsPage? CreateOptionsPage(ToolSettings settings, IOptionsHost host) => null;
}

/// <summary>Flatten / Z = 0 (<see cref="FlattenOperation"/>).</summary>
internal sealed class FlattenOperationFactory : IOperationFactory
{
    /// <inheritdoc/>
    public string Id => FlattenOperation.OperationId;

    /// <inheritdoc/>
    public string DisplayName => "Flatten (Z = 0)";

    /// <inheritdoc/>
    public string Description => "Moves the selected objects to elevation zero. Floating blocks are moved down by their insertion point.";

    /// <inheritdoc/>
    public string CommandKeyword => "Flatten";

    /// <inheritdoc/>
    public bool UsesSelection => true;

    public ICleanOperation Create(AcadSession session, ToolSettings settings) =>
        new FlattenOperation(
            new AcadFlattenGateway(session, FlattenerRegistry.CreateDefault(settings.Flatten.ZTolerance.Value)),
            settings.Flatten);

    /// <inheritdoc/>
    public IOptionsPage CreateOptionsPage(ToolSettings settings, IOptionsHost host) => new FlattenOptionsPage(settings);
}

/// <summary>Duplicate removal (<see cref="DuplicateOperation"/>).</summary>
internal sealed class DuplicateOperationFactory : IOperationFactory
{
    /// <inheritdoc/>
    public string Id => DuplicateOperation.OperationId;

    /// <inheritdoc/>
    public string DisplayName => "Duplicate objects";

    /// <inheritdoc/>
    public string Description => "Removes overlapping copies of lines, circles, arcs and polylines within the selection. Copies are only compared within the same space or block definition.";

    /// <inheritdoc/>
    public string CommandKeyword => "Duplicates";

    /// <inheritdoc/>
    public bool UsesSelection => true;

    public ICleanOperation Create(AcadSession session, ToolSettings settings) =>
        new DuplicateOperation(
            new AcadShapeGateway(session, ShapeReaderRegistry.CreateDefault()),
            settings.Duplicates);

    /// <inheritdoc/>
    public IOptionsPage CreateOptionsPage(ToolSettings settings, IOptionsHost host) => new DuplicateOptionsPage(settings);
}

/// <summary>Unused layer purge (<see cref="LayerPurgeOperation"/>).</summary>
internal sealed class LayerPurgeOperationFactory : IOperationFactory
{
    /// <inheritdoc/>
    public string Id => LayerPurgeOperation.OperationId;

    /// <inheritdoc/>
    public string DisplayName => "Unused layers (whole drawing)";

    /// <inheritdoc/>
    public string Description => "Deletes layers that are not used by any object. Applies to the whole drawing, not just the selection. Layer 0, Defpoints, the current layer and xref layers are always kept.";

    /// <inheritdoc/>
    public string CommandKeyword => "Layers";

    /// <inheritdoc/>
    public bool UsesSelection => false;

    public ICleanOperation Create(AcadSession session, ToolSettings settings) =>
        new LayerPurgeOperation(new AcadLayerGateway(session), settings.Layers);

    /// <inheritdoc/>
    public IOptionsPage CreateOptionsPage(ToolSettings settings, IOptionsHost host) => new LayerPurgeOptionsPage(settings);
}
