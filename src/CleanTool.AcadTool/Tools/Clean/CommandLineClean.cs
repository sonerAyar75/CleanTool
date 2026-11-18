using Autodesk.AutoCAD.EditorInput;
using CleanTool.AcadTool.Composition;
using CleanTool.AcadTool.UI;
using CleanTool.AcadTool.Infrastructure;
using CleanTool.Core.Geometry;
using CleanTool.Core.Model;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Operations.Layers;
using CleanTool.Core.Pipeline;

namespace CleanTool.AcadTool.Tools.Clean;

/// <summary>
/// -CT_CLEAN: the command-line version without a dialog (suitable for scripts and batch use).
///
///   Current settings: Operations = Flatten, Duplicates, Layers  Tolerance = 0.0001  Ignore = None  Blocks = No
///   Enter an option [Tolerance/Ignore/Blocks/OVerlap/Operations/Preview/Run] &lt;Run&gt;:
///
/// The operation list comes from the registry; this file does not change when an operation is added.
/// </summary>
internal sealed class CommandLineClean
{
    private const string KwTolerance = "Tolerance";
    private const string KwIgnore = "Ignore";
    private const string KwBlocks = "Blocks";
    private const string KwOverlap = "OVerlap"; // abbreviation "OV"; "O" belongs to Operations

    private static readonly (string Keyword, BlockOverlapMode Mode)[] OverlapKeywords =
    {
        ("Loose", BlockOverlapMode.RemoveLooseObject),
        ("Block", BlockOverlapMode.RemoveFromBlock),
        ("Off", BlockOverlapMode.DontCompare)
    };

    /// <summary>Keywords of the Ignore option (no spaces; capital letters form the abbreviation).</summary>
    private static readonly (string Keyword, ComparedProperty Property)[] IgnoreKeywords =
    {
        ("Layer", ComparedProperty.Layer),
        ("Color", ComparedProperty.Color),
        ("LType", ComparedProperty.Linetype),
        ("LtScale", ComparedProperty.LinetypeScale),
        ("LWeight", ComparedProperty.Lineweight),
        ("Transparency", ComparedProperty.Transparency)
    };

    private static readonly (string Keyword, BlockMode Mode)[] BlockKeywords =
    {
        ("No", BlockMode.None),
        ("Selected", BlockMode.SelectedBlocks),
        ("All", BlockMode.AllBlocks)
    };
    private const string KwOperations = "Operations";
    private const string KwPreview = "Preview";
    private const string KwRun = "Run";

    private readonly Editor _editor;
    private readonly IReadOnlyList<IOperationFactory> _factories;
    private readonly SettingsStore _store;
    private readonly CleanAction _clean;

    private ToolSettings _settings;
    private readonly HashSet<string> _enabled;

    /// <summary>Creates the command-line flow.</summary>
    /// <param name="editor">Command line to prompt on.</param>
    /// <param name="factories">The tool's operations (listed under the Operations option).</param>
    /// <param name="store">Settings shared with the dialog.</param>
    /// <param name="clean">Runs the selected operations on the selection.</param>
    public CommandLineClean(Editor editor, IReadOnlyList<IOperationFactory> factories, SettingsStore store, CleanAction clean)
    {
        _editor = editor;
        _factories = factories;
        _store = store;
        _clean = clean;

        _settings = store.Current;
        _enabled = new HashSet<string>(factories.Where(f => store.IsEnabled(f.Id)).Select(f => f.Id), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Prompts for options until the user runs or cancels.</summary>
    public void Run()
    {
        while (true)
        {
            WriteCurrentSettings();

            var options = new PromptKeywordOptions(
                $"\nEnter an option [{KwTolerance}/{KwIgnore}/{KwBlocks}/{KwOverlap}/{KwOperations}/{KwPreview}/{KwRun}] <{KwRun}>: ",
                $"{KwTolerance} {KwIgnore} {KwBlocks} {KwOverlap} {KwOperations} {KwPreview} {KwRun}")
            {
                AllowNone = true
            };

            var result = _editor.GetKeywords(options);
            if (result.Status == PromptStatus.Cancel) return;

            var keyword = result.Status == PromptStatus.None ? KwRun : result.StringResult;

            switch (keyword)
            {
                case KwTolerance:
                    AskTolerance();
                    break;

                case KwIgnore:
                    AskIgnoredProperties();
                    break;

                case KwBlocks:
                    AskBlocks();
                    break;

                case KwOverlap:
                    AskOverlap();
                    break;

                case KwOperations:
                    AskOperations();
                    break;

                case KwPreview:
                    Execute(dryRun: true);
                    break;

                case KwRun:
                    if (Execute(dryRun: false)) return;
                    break;
            }
        }
    }

    private void WriteCurrentSettings()
    {
        var operations = _factories.Where(f => _enabled.Contains(f.Id)).Select(f => f.CommandKeyword).ToList();
        var operationText = operations.Count == 0 ? "(none)" : string.Join(", ", operations);

        _editor.WriteMessage(
            $"\nCurrent settings: Operations = {operationText}  " +
            $"Tolerance = {_settings.Duplicates.Tolerance}  " +
            $"Ignore = {IgnoredText()}  " +
            $"Blocks = {BlockKeywords.First(b => b.Mode == _settings.Blocks).Keyword}  " +
            $"Overlap = {OverlapKeywords.First(o => o.Mode == _settings.Duplicates.BlockOverlap).Keyword}");
    }

    private void AskTolerance()
    {
        var options = new PromptDoubleOptions($"\nDuplicate tolerance <{_settings.Duplicates.Tolerance}>: ")
        {
            AllowNegative = false,
            AllowZero = false,
            AllowNone = true
        };

        var result = _editor.GetDouble(options);
        if (result.Status != PromptStatus.OK) return;

        _settings = _settings with { Duplicates = _settings.Duplicates with { Tolerance = new Tolerance(result.Value) } };
    }

    private string IgnoredText()
    {
        var ignored = IgnoreKeywords
            .Where(k => _settings.Duplicates.IgnoredProperties.HasFlag(k.Property))
            .Select(k => k.Keyword)
            .ToList();
        return ignored.Count == 0 ? "None" : string.Join(",", ignored);
    }

    /// <summary>Toggles the properties ignored when comparing duplicates; Enter finishes.</summary>
    private void AskIgnoredProperties()
    {
        var keywords = IgnoreKeywords.Select(k => k.Keyword).ToList();

        while (true)
        {
            _editor.WriteMessage($"\nIgnored when comparing duplicates: {IgnoredText()}");

            var options = new PromptKeywordOptions(
                $"\nToggle a property [{string.Join("/", keywords)}] <done>: ",
                string.Join(" ", keywords))
            {
                AllowNone = true
            };

            var result = _editor.GetKeywords(options);
            if (result.Status != PromptStatus.OK) return;

            var property = IgnoreKeywords.First(k => k.Keyword == result.StringResult).Property;
            var ignored = _settings.Duplicates.IgnoredProperties ^ property; // toggle

            _settings = _settings with { Duplicates = _settings.Duplicates with { IgnoredProperties = ignored } };
        }
    }

    private void AskBlocks()
    {
        var current = BlockKeywords.First(b => b.Mode == _settings.Blocks).Keyword;
        var keywords = BlockKeywords.Select(b => b.Keyword).ToList();

        var options = new PromptKeywordOptions(
            $"\nInclude block definitions [{string.Join("/", keywords)}] <{current}>: ",
            string.Join(" ", keywords))
        {
            AllowNone = true
        };

        var result = _editor.GetKeywords(options);
        if (result.Status != PromptStatus.OK) return;

        _settings = _settings with { Blocks = BlockKeywords.First(b => b.Keyword == result.StringResult).Mode };
    }

    /// <summary>When a loose object duplicates block content: Loose = erase the loose copy, Block = erase from the block, Off = do not compare.</summary>
    private void AskOverlap()
    {
        var current = OverlapKeywords.First(o => o.Mode == _settings.Duplicates.BlockOverlap).Keyword;
        var keywords = OverlapKeywords.Select(o => o.Keyword).ToList();

        var options = new PromptKeywordOptions(
            $"\nObject overlapping block content: remove [{string.Join("/", keywords)}] <{current}>: ",
            string.Join(" ", keywords))
        {
            AllowNone = true
        };

        var result = _editor.GetKeywords(options);
        if (result.Status != PromptStatus.OK) return;

        var mode = OverlapKeywords.First(o => o.Keyword == result.StringResult).Mode;
        _settings = _settings with { Duplicates = _settings.Duplicates with { BlockOverlap = mode } };
        if (mode == BlockOverlapMode.DontCompare) return;

        var match = _settings.Duplicates.MatchPropertiesForBlockOverlap ? "Yes" : "No";
        var matchOptions = new PromptKeywordOptions(
            $"\nAlso match layer/color/linetype with block content? [Yes/No] <{match}>: ", "Yes No")
        {
            AllowNone = true
        };

        var matchResult = _editor.GetKeywords(matchOptions);
        if (matchResult.Status != PromptStatus.OK) return;

        _settings = _settings with
        {
            Duplicates = _settings.Duplicates with { MatchPropertiesForBlockOverlap = matchResult.StringResult == "Yes" }
        };
    }

    /// <summary>Turns operations on and off one by one; Enter finishes.</summary>
    private void AskOperations()
    {
        var keywords = string.Join("/", _factories.Select(f => f.CommandKeyword));

        while (true)
        {
            CommandLine.WriteLines(_editor, _factories.Select(factory =>
                $"  {(_enabled.Contains(factory.Id) ? "On " : "Off")}  {factory.CommandKeyword} - {factory.DisplayName}"));

            var options = new PromptKeywordOptions(
                $"\nToggle an operation [{keywords}] <done>: ",
                string.Join(" ", _factories.Select(f => f.CommandKeyword)))
            {
                AllowNone = true
            };

            var result = _editor.GetKeywords(options);
            if (result.Status != PromptStatus.OK) return;

            var selected = _factories.First(f => f.CommandKeyword == result.StringResult);
            if (!_enabled.Remove(selected.Id)) _enabled.Add(selected.Id);
        }
    }

    /// <returns>true if the operations ran; false if no operation is selected.</returns>
    private bool Execute(bool dryRun)
    {
        var ids = _factories.Where(f => _enabled.Contains(f.Id)).Select(f => f.Id).ToList();
        if (ids.Count == 0)
        {
            _editor.WriteMessage("\nNo operations selected. Use the Operations option to turn some on.");
            return false;
        }

        _store.Update(_settings, ids);

        var report = _clean(ids, dryRun, _settings);
        CommandLine.WriteReport(_editor, report);

        return true;
    }
}
