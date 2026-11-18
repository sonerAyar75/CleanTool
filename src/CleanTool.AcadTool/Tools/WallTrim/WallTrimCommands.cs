using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using CleanTool.AcadTool.Composition;
using CleanTool.AcadTool.Hosting;
using CleanTool.AcadTool.Infrastructure;
using CleanTool.AcadTool.UI;
using CleanTool.Core.Operations.BlockTrim;
using CleanTool.Core.Operations.Layers;
using CleanTool.Core.Pipeline;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(CleanTool.AcadTool.Tools.WallTrim.WallTrimCommands))]

namespace CleanTool.AcadTool.Tools.WallTrim;

/// <summary>
/// WallTrim commands. Select the wall lines and the column blocks together; the lines are cut at the column faces.
/// If the module failed to load, every command reports why and exits (Clean is not affected).
/// </summary>
public sealed class WallTrimCommands
{
    private const string Group = "CLEANTOOL";
    private const CommandFlags SelectionFlags = CommandFlags.Modal | CommandFlags.UsePickSet;

    /// <summary>Opens the dialog first; objects are selected with "Select objects" (objects selected beforehand are the initial selection).</summary>
    [CommandMethod(Group, "CT_WALLTRIM", SelectionFlags)]
    public void WallTrimWithDialog()
    {
        if (!TryStart(out var document, out var tool)) return;

        var session = new AcadCleanSession(document, tool.Runner, SelectionScope.FromImplied(document.Editor));

        using var form = new CleanToolForm(tool.Registry.All, tool.Settings, session, WallTrimModule.FormOptions);
        AcApp.ShowModalDialog(form);
    }

    /// <summary>Short alias of CT_WALLTRIM.</summary>
    [CommandMethod(Group, "TTW", SelectionFlags)]
    public void WallTrimWithDialogAlias() => WallTrimWithDialog();

    /// <summary>Short alias of -CT_WALLTRIM.</summary>
    [CommandMethod(Group, "-TTW", SelectionFlags)]
    public void WallTrimFromCommandLineAlias() => WallTrimFromCommandLine();

    /// <summary>
    /// Command line version (suitable for scripts):
    ///   Current settings: Trim inside = K*  Match properties = No  Open junctions = No  Closed polylines = No
    ///   Enter an option [Inside/Match/Junctions/Closed/Thickness/Keep/Layers/Preview/Run] &lt;Run&gt;:
    /// </summary>
    [CommandMethod(Group, "-CT_WALLTRIM", SelectionFlags)]
    public void WallTrimFromCommandLine()
    {
        if (!TryStart(out var document, out var tool)) return;

        var editor = document.Editor;
        var scope = SelectionScope.Prompt(editor);
        if (scope is null) return;

        editor.WriteMessage($"\n{scope.Description}.");

        var settings = tool.Settings.Current;
        var ids = tool.Registry.All.Select(f => f.Id).ToList();

        while (true)
        {
            var options = settings.WallTrim;
            var inside = options.TrimInsideBlocks.Count == 0 ? "none" : LayerPurgeOptions.FormatPatterns(options.TrimInsideBlocks);
            var outlines = options.CreateClosedOutlines
                ? $"Yes (max thickness {options.MaxWallThickness:G}, keep lines {(options.KeepOriginalLines ? "Yes" : "No")})"
                : "No";
            editor.WriteMessage(
                $"\nCurrent settings: Trim inside = {inside}  Match properties = {(options.MatchProperties ? "Yes" : "No")}" +
                $"  Open junctions = {(options.OpenJunctions ? "Yes" : "No")}  Closed polylines = {outlines}" +
                $"  Wall layers = {(options.WallLayers.Count == 0 ? "all" : LayerPurgeOptions.FormatPatterns(options.WallLayers))}");

            var prompt = new PromptKeywordOptions(
                "\nEnter an option [Inside/Match/Junctions/Closed/Thickness/Keep/Layers/Preview/Run] <Run>: ",
                "Inside Match Junctions Closed Thickness Keep Layers Preview Run")
            {
                AllowNone = true
            };

            var result = editor.GetKeywords(prompt);
            if (result.Status == PromptStatus.Cancel) return;

            switch (result.Status == PromptStatus.None ? "Run" : result.StringResult)
            {
                case "Inside":
                    settings = AskInside(editor, settings);
                    break;

                case "Match":
                    settings = AskMatch(editor, settings);
                    break;

                case "Junctions":
                    settings = AskYesNo(editor, settings, "Open T and cross wall junctions?",
                        settings.WallTrim.OpenJunctions, (o, v) => o with { OpenJunctions = v });
                    break;

                case "Closed":
                    settings = AskYesNo(editor, settings, "Create closed polylines from the wall lines?",
                        settings.WallTrim.CreateClosedOutlines, (o, v) => o with { CreateClosedOutlines = v });
                    break;

                case "Thickness":
                    settings = AskThickness(editor, settings);
                    break;

                case "Keep":
                    settings = AskYesNo(editor, settings, "Keep the original lines next to the closed polylines?",
                        settings.WallTrim.KeepOriginalLines, (o, v) => o with { KeepOriginalLines = v });
                    break;

                case "Layers":
                    settings = AskWallLayers(editor, settings);
                    break;

                case "Preview":
                    tool.Settings.Update(settings, ids);
                    Report(editor, tool.Runner.Run(document, scope, ids, dryRun: true, settings));
                    break;

                case "Run":
                    tool.Settings.Update(settings, ids);
                    Report(editor, tool.Runner.Run(document, scope, ids, dryRun: false, settings));
                    return;
            }
        }
    }

    private static ToolSettings AskInside(Editor editor, ToolSettings settings)
    {
        var current = LayerPurgeOptions.FormatPatterns(settings.WallTrim.TrimInsideBlocks);
        var prompt = new PromptStringOptions(
            $"\nTrim lines inside blocks named (e.g. K*;KOLON*, . for none) <{(current.Length == 0 ? "none" : current)}>: ")
        {
            AllowSpaces = true
        };

        var result = editor.GetString(prompt);
        if (result.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(result.StringResult)) return settings;

        var patterns = result.StringResult.Trim() == "."
            ? Array.Empty<string>()
            : LayerPurgeOptions.ParsePatterns(result.StringResult);

        if (patterns.Any(p => p is "true" or "false" or "yes" or "no" or "1" or "0"))
        {
            editor.WriteMessage("\nEnter block names (e.g. K*;KOLON*), not true/false. Setting not changed.");
            return settings;
        }

        return settings with { WallTrim = settings.WallTrim with { TrimInsideBlocks = patterns } };
    }

    private static ToolSettings AskYesNo(
        Editor editor, ToolSettings settings, string question, bool current, Func<BlockTrimOptions, bool, BlockTrimOptions> apply)
    {
        var prompt = new PromptKeywordOptions($"\n{question} [Yes/No] <{(current ? "Yes" : "No")}>: ", "Yes No")
        {
            AllowNone = true
        };

        var result = editor.GetKeywords(prompt);
        if (result.Status != PromptStatus.OK) return settings;

        return settings with { WallTrim = apply(settings.WallTrim, result.StringResult == "Yes") };
    }

    private static ToolSettings AskWallLayers(Editor editor, ToolSettings settings)
    {
        var current = LayerPurgeOptions.FormatPatterns(settings.WallTrim.WallLayers);
        var prompt = new PromptStringOptions(
            $"\nWall layers (e.g. DUVAR*;*WALL*, . for all) <{(current.Length == 0 ? "all" : current)}>: ")
        {
            AllowSpaces = true
        };

        var result = editor.GetString(prompt);
        if (result.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(result.StringResult)) return settings;

        var patterns = result.StringResult.Trim() == "."
            ? Array.Empty<string>()
            : LayerPurgeOptions.ParsePatterns(result.StringResult);

        return settings with { WallTrim = settings.WallTrim with { WallLayers = patterns } };
    }

    private static ToolSettings AskThickness(Editor editor, ToolSettings settings)
    {
        var prompt = new PromptDoubleOptions("\nLargest wall thickness (drawing units)")
        {
            AllowNone = true,
            AllowNegative = false,
            AllowZero = false,
            DefaultValue = settings.WallTrim.MaxWallThickness,
            UseDefaultValue = true
        };

        var result = editor.GetDouble(prompt);
        if (result.Status != PromptStatus.OK) return settings;

        return settings with { WallTrim = settings.WallTrim with { MaxWallThickness = result.Value } };
    }

    private static ToolSettings AskMatch(Editor editor, ToolSettings settings)
    {
        var current = settings.WallTrim.MatchProperties ? "Yes" : "No";
        var prompt = new PromptKeywordOptions($"\nLayer/color/linetype must match the block? [Yes/No] <{current}>: ", "Yes No")
        {
            AllowNone = true
        };

        var result = editor.GetKeywords(prompt);
        if (result.Status != PromptStatus.OK) return settings;

        return settings with { WallTrim = settings.WallTrim with { MatchProperties = result.StringResult == "Yes" } };
    }

    /// <summary>Is there an active document and is the WallTrim module loaded? Otherwise writes the reason.</summary>
    private static bool TryStart(out Document document, out WallTrimModule tool)
    {
        tool = null!;
        document = AcApp.DocumentManager.MdiActiveDocument;
        return document is not null && ToolHost.TryGet(document.Editor, out tool);
    }

    private static void Report(Editor editor, CleanReport report) => CommandLine.WriteReport(editor, report);
}
