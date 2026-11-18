using Autodesk.AutoCAD.EditorInput;
using CleanTool.Core.Pipeline;

namespace CleanTool.AcadTool.Infrastructure;

/// <summary>
/// Multi-line output to the command line (and the F2 text window).
///
/// Why a single call? Writing lines one by one with WriteMessage("\n" + line) makes recent AutoCAD versions add their
/// own line break after each message, so the output appears double spaced. Joining all lines and writing them with one
/// WriteMessage avoids that. Every command uses this class.
/// </summary>
internal static class CommandLine
{
    /// <summary>Writes the lines as one message (each on its own line, without blank lines in between).</summary>
    public static void WriteLines(Editor editor, IEnumerable<string> lines)
    {
        var text = string.Join("\n", lines);
        if (text.Length > 0) editor.WriteMessage("\n" + text);
    }

    public static void WriteReport(Editor editor, CleanReport report) =>
        WriteLines(editor, CleanReportFormatter.Format(report));
}
