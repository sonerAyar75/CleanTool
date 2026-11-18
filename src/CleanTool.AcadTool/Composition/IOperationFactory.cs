using CleanTool.AcadTool.Infrastructure;
using CleanTool.AcadTool.UI;
using CleanTool.Core.Abstractions;

namespace CleanTool.AcadTool.Composition;

/// <summary>
/// The single AutoCAD-side entry point of an operation (used by every tool module):
/// creates the operation with its gateway and provides the name, description and options page shown in the dialog.
/// The dialog is built from this information, so new features never require changes to the form code.
/// </summary>
internal interface IOperationFactory
{
    string Id { get; }

    string DisplayName { get; }

    string Description { get; }

    /// <summary>
    /// true if the operation needs a selection; false if it applies to the whole drawing (e.g. unused layers).
    /// </summary>
    bool UsesSelection { get; }

    /// <summary>Single-word keyword (no spaces) used on the command line (e.g. -CT_CLEAN Operations).</summary>
    string CommandKeyword { get; }

    ICleanOperation Create(AcadSession session, ToolSettings settings);

    /// <summary>Creates the options page shown under the operation in the dialog; null for operations without options.</summary>
    IOptionsPage? CreateOptionsPage(ToolSettings settings, IOptionsHost host);
}
