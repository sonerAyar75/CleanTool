using Autodesk.AutoCAD.Runtime;
using CleanTool.AcadTool.Hosting;
using CleanTool.AcadTool.Infrastructure;
using CleanTool.AcadTool.Tools.Clean;
using CleanTool.AcadTool.Tools.WallTrim;
using CleanTool.Core.Hosting;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(CleanTool.AcadTool.CleanToolApp))]

namespace CleanTool.AcadTool;

/// <summary>
/// Entry point of the plug-in: loads the tool modules. Each module owns its operations, settings and commands;
/// if one fails to load, the others keep working.
///
/// A new tool = an IToolModule + a command class under Tools/&lt;Name&gt;/ and one line in the list below.
/// Command classes register themselves in their own files with [assembly: CommandClass].
/// </summary>
public sealed class CleanToolApp : IExtensionApplication
{
    /// <summary>Called by AutoCAD when the DLL is loaded: loads the tool modules and reports any module that failed.</summary>
    public void Initialize()
    {
        var statuses = ToolHost.Load(new IToolModule[]
        {
            new CleanModule(),
            new WallTrimModule()
        });

        var editor = AcApp.DocumentManager.MdiActiveDocument?.Editor;
        if (editor is null) return;

        var lines = new List<string> { "CleanTool loaded. Commands: TTC / CT_CLEAN (cleanup), TTW / CT_WALLTRIM (cut walls at columns)." };
        lines.AddRange(statuses.Where(s => !s.IsLoaded).Select(s => $"[!] {s}"));
        CommandLine.WriteLines(editor, lines);
    }

    /// <summary>Called by AutoCAD on shutdown: terminates all modules.</summary>
    public void Terminate() => ToolHost.TerminateAll();
}
