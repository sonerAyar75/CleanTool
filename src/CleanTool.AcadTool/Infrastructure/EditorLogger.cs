using Autodesk.AutoCAD.EditorInput;
using CleanTool.Core.Abstractions;

namespace CleanTool.AcadTool.Infrastructure;

/// <summary>Connects Core's logger port to the AutoCAD command line.</summary>
internal sealed class EditorLogger : ICleanLogger
{
    private readonly Editor _editor;

    /// <summary>Creates a logger that writes to the given editor.</summary>
    public EditorLogger(Editor editor)
    {
        _editor = editor;
    }

    /// <inheritdoc/>
    public void Info(string message) => _editor.WriteMessage("\n" + message);

    /// <inheritdoc/>
    public void Warn(string message) => _editor.WriteMessage("\n[!] " + message);
}
