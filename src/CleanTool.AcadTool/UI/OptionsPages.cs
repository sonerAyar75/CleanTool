using System.Globalization;
using System.Windows.Forms;
using CleanTool.AcadTool.Composition;
using CleanTool.Core.Geometry;
using CleanTool.Core.Model;
using CleanTool.Core.Operations.Duplicates;
using CleanTool.Core.Operations.Layers;

namespace CleanTool.AcadTool.UI;

/// <summary>
/// Options page of an operation. The form only knows this interface, not which settings exist.
/// </summary>
internal interface IOptionsPage
{
    Control View { get; }

    /// <summary>
    /// Validates the input and applies it to the settings. If invalid, returns false and an error to show the user.
    /// </summary>
    bool TryApply(ToolSettings current, out ToolSettings updated, out string? error);
}

/// <summary>Base class for options pages made of label + control rows; the height follows the content.</summary>
internal abstract class OptionsPageBase : IOptionsPage
{
    private readonly TableLayoutPanel _grid;

    /// <summary>Creates an empty page.</summary>
    protected OptionsPageBase()
    {
        _grid = new TableLayoutPanel
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(20, 0, 0, 4)
        };
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    }

    /// <inheritdoc/>
    public Control View => _grid;

    /// <inheritdoc/>
    public abstract bool TryApply(ToolSettings current, out ToolSettings updated, out string? error);

    /// <summary>Adds a label + text box row.</summary>
    /// <param name="label">Label text.</param>
    /// <param name="value">Initial text.</param>
    /// <param name="toolTip">Optional tooltip on the label and the box.</param>
    protected TextBox AddTextBox(string label, string value, string? toolTip = null)
    {
        var row = NewRow();
        var labelControl = CreateLabel(label);
        _grid.Controls.Add(labelControl, 0, row);

        var box = new TextBox
        {
            Text = value,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 2, 0, 2)
        };
        _grid.Controls.Add(box, 1, row);

        if (toolTip is not null)
        {
            var tip = new ToolTip { AutoPopDelay = 20000 };
            tip.SetToolTip(labelControl, toolTip);
            tip.SetToolTip(box, toolTip);
            box.Disposed += (_, _) => tip.Dispose();
        }

        return box;
    }

    /// <summary>
    /// Adds a small button to the right of a text box (e.g. "..." to pick from a list).
    /// The text box and the button share one cell; the box takes the remaining width.
    /// </summary>
    protected Button AttachButton(TextBox box, string text, string? toolTip = null)
    {
        var cell = box.Parent as TableLayoutPanel
            ?? throw new InvalidOperationException("The text box must be added with AddTextBox first.");
        var position = cell.GetPositionFromControl(box);
        cell.Controls.Remove(box);

        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = Padding.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new System.Drawing.Size(26, box.Height),
            Margin = new Padding(4, 2, 0, 2)
        };

        if (toolTip is not null)
        {
            var tip = new ToolTip();
            tip.SetToolTip(button, toolTip);
            button.Disposed += (_, _) => tip.Dispose();
        }

        row.Controls.Add(box, 0, 0);
        row.Controls.Add(button, 1, 0);
        cell.Controls.Add(row, position.Column, position.Row);
        return button;
    }

    /// <summary>A label + checkboxes laid out in a grid (like OVERKILL's "Ignore object property").</summary>
    protected IReadOnlyList<CheckBox> AddCheckBoxGrid(string label, IEnumerable<(string Text, bool Checked)> items, int columns)
    {
        var row = NewRow();
        _grid.Controls.Add(CreateLabel(label), 0, row);

        var grid = new TableLayoutPanel
        {
            ColumnCount = columns,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 2, 0, 2)
        };
        for (var c = 0; c < columns; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var boxes = new List<CheckBox>();
        foreach (var (text, isChecked) in items)
        {
            var box = new CheckBox { Text = text, Checked = isChecked, AutoSize = true, Margin = new Padding(0, 1, 10, 1) };
            grid.Controls.Add(box, boxes.Count % columns, boxes.Count / columns);
            boxes.Add(box);
        }

        _grid.Controls.Add(grid, 1, row);
        return boxes;
    }

    /// <summary>A single checkbox without a label (in the value column, below the previous setting).</summary>
    protected CheckBox AddCheckBox(string text, bool value, string? toolTip = null)
    {
        var row = NewRow();
        var box = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };

        if (toolTip is not null)
        {
            var tip = new ToolTip { AutoPopDelay = 15000 };
            tip.SetToolTip(box, toolTip);
            box.Disposed += (_, _) => tip.Dispose();
        }

        _grid.Controls.Add(box, 1, row);
        return box;
    }

    /// <summary>A label + drop-down list. Items are (value, text) pairs.</summary>
    protected ComboBox AddComboBox<T>(string label, IEnumerable<(T Value, string Text)> items, T selected, string? toolTip = null)
        where T : notnull
    {
        var row = NewRow();
        var labelControl = CreateLabel(label);
        _grid.Controls.Add(labelControl, 0, row);

        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 2, 0, 2)
        };
        foreach (var (value, text) in items) combo.Items.Add(new ComboItem<T>(value, text));
        combo.SelectedItem = combo.Items.Cast<ComboItem<T>>().FirstOrDefault(i => EqualityComparer<T>.Default.Equals(i.Value, selected));
        if (combo.SelectedIndex < 0 && combo.Items.Count > 0) combo.SelectedIndex = 0;

        if (toolTip is not null)
        {
            var tip = new ToolTip { AutoPopDelay = 15000 };
            tip.SetToolTip(labelControl, toolTip);
            tip.SetToolTip(combo, toolTip);
            combo.Disposed += (_, _) => tip.Dispose();
        }

        _grid.Controls.Add(combo, 1, row);
        return combo;
    }

    /// <summary>The value of the selected item of a combo box created with AddComboBox.</summary>
    protected static T SelectedValue<T>(ComboBox combo) where T : notnull => ((ComboItem<T>)combo.SelectedItem!).Value;

    /// <summary>Parses a tolerance field; on failure focuses the field and returns a user-facing error.</summary>
    protected static bool TryReadTolerance(TextBox box, string fieldName, out Tolerance tolerance, out string? error)
    {
        if (Tolerance.TryParse(box.Text, out tolerance))
        {
            error = null;
            return true;
        }

        error = $"{fieldName} must be a positive number (e.g. 0.001).";
        box.Focus();
        box.SelectAll();
        return false;
    }

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left | AnchorStyles.Top,
        Margin = new Padding(0, 5, 10, 2)
    };

    private sealed record ComboItem<T>(T Value, string Text)
    {
        public override string ToString() => Text;
    }

    private int NewRow()
    {
        var row = _grid.RowCount;
        _grid.RowCount = row + 1;
        _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return row;
    }
}

/// <summary>Options of Flatten (Z tolerance).</summary>
internal sealed class FlattenOptionsPage : OptionsPageBase
{
    private readonly TextBox _tolerance;

    /// <summary>Creates the page from the current settings.</summary>
    public FlattenOptionsPage(ToolSettings settings)
    {
        _tolerance = AddTextBox("Z tolerance", settings.Flatten.ZTolerance.ToString());
    }

    public override bool TryApply(ToolSettings current, out ToolSettings updated, out string? error)
    {
        updated = current;
        if (!TryReadTolerance(_tolerance, "Z tolerance", out var tolerance, out error)) return false;

        updated = current with { Flatten = current.Flatten with { ZTolerance = tolerance } };
        return true;
    }
}

/// <summary>Options of duplicate removal (tolerance, ignored properties, block overlap).</summary>
internal sealed class DuplicateOptionsPage : OptionsPageBase
{
    /// <summary>Display order and labels. Supporting a new property only needs a new row here.</summary>
    private static readonly (ComparedProperty Property, string Label)[] IgnorableProperties =
    {
        (ComparedProperty.Layer, "Layer"),
        (ComparedProperty.Color, "Color"),
        (ComparedProperty.Linetype, "Linetype"),
        (ComparedProperty.LinetypeScale, "Lt scale"),
        (ComparedProperty.Lineweight, "Lineweight"),
        (ComparedProperty.Transparency, "Transparency")
    };

    private readonly TextBox _tolerance;
    private readonly IReadOnlyList<CheckBox> _ignore;
    private readonly ComboBox _blockOverlap;
    private readonly CheckBox _matchOverlapProperties;

    /// <summary>Creates the page from the current settings.</summary>
    public DuplicateOptionsPage(ToolSettings settings)
    {
        _tolerance = AddTextBox("Tolerance", settings.Duplicates.Tolerance.ToString());

        var ignored = settings.Duplicates.IgnoredProperties;
        _ignore = AddCheckBoxGrid(
            "Ignore",
            IgnorableProperties.Select(p => (p.Label, ignored.HasFlag(p.Property))),
            columns: 3);

        _blockOverlap = AddComboBox(
            "Block overlap",
            new[]
            {
                (BlockOverlapMode.RemoveLooseObject, "Remove loose object"),
                (BlockOverlapMode.RemoveFromBlock, "Remove from block"),
                (BlockOverlapMode.DontCompare, "Don't compare")
            },
            settings.Duplicates.BlockOverlap,
            "When a loose object is an exact copy of an object inside a block:\n" +
            "Remove loose object - deletes the loose copy; the block is not changed (recommended).\n" +
            "Remove from block - deletes it from the block definition; only for blocks used once,\n" +
            "    because changing a definition changes every insert.\n" +
            "Don't compare - block content and loose objects are not compared.\n" +
            "To cut lines that only partly overlap blocks (e.g. walls over columns) use CT_WALLTRIM.");

        _matchOverlapProperties = AddCheckBox(
            "Match properties",
            settings.Duplicates.MatchPropertiesForBlockOverlap,
            "Off (recommended): only geometry is compared with block content.\n" +
            "On: layer/color/linetype must also match (the Ignore options apply).");

        void UpdateEnabled() =>
            _matchOverlapProperties.Enabled = SelectedValue<BlockOverlapMode>(_blockOverlap) != BlockOverlapMode.DontCompare;

        _blockOverlap.SelectedIndexChanged += (_, _) => UpdateEnabled();
        UpdateEnabled();
    }

    public override bool TryApply(ToolSettings current, out ToolSettings updated, out string? error)
    {
        updated = current;
        if (!TryReadTolerance(_tolerance, "Tolerance", out var tolerance, out error)) return false;

        var ignored = ComparedProperty.None;
        for (var i = 0; i < IgnorableProperties.Length; i++)
        {
            if (_ignore[i].Checked) ignored |= IgnorableProperties[i].Property;
        }

        updated = current with
        {
            Duplicates = current.Duplicates with
            {
                Tolerance = tolerance,
                IgnoredProperties = ignored,
                BlockOverlap = SelectedValue<BlockOverlapMode>(_blockOverlap),
                MatchPropertiesForBlockOverlap = _matchOverlapProperties.Checked
            }
        };
        return true;
    }
}

/// <summary>WallTrim: settings for cutting lines that run over or through blocks (columns).</summary>
internal sealed class BlockTrimOptionsPage : OptionsPageBase
{
    /// <summary>Catches the field being mistaken for a switch and "true" typed into it (field feedback).</summary>
    private static readonly string[] SwitchWords = { "true", "false", "yes", "no", "on", "off", "1", "0", "evet", "hayır", "hayir" };

    private readonly IOptionsHost _host;
    private readonly TextBox _trimInside;
    private readonly CheckBox _matchProperties;
    private readonly CheckBox _openJunctions;
    private readonly TextBox _wallLayers;
    private readonly Button _pickLayers;
    private readonly CheckBox _createOutlines;
    private readonly TextBox _maxThickness;
    private readonly CheckBox _keepOriginals;

    /// <summary>Creates the page from the current settings.</summary>
    /// <param name="settings">Current settings.</param>
    /// <param name="host">Provides the block list for the "..." picker.</param>
    public BlockTrimOptionsPage(ToolSettings settings, IOptionsHost host)
    {
        _host = host;

        _trimInside = AddTextBox(
            "Trim inside blocks",
            LayerPurgeOptions.FormatPatterns(settings.WallTrim.TrimInsideBlocks),
            "Block names (wildcards * and ?, separated by ;) whose closed outline cuts lines,\n" +
            "e.g. columns: K*; KOLON*\n" +
            "Lines crossing these blocks are cut at the block face; the part inside is removed.\n" +
            "Only closed straight polylines and circles count as outlines.\n" +
            "Leave empty to only trim along block edges. Do not use * alone:\n" +
            "a title block frame would remove everything inside it.");
        _trimInside.PlaceholderText = "block names, e.g. K*; KOLON*";

        var pick = AttachButton(_trimInside, "...", "Choose blocks from a list");
        pick.Click += (_, _) => PickBlocks();

        _matchProperties = AddCheckBox(
            "Match properties",
            settings.WallTrim.MatchProperties,
            "Off (recommended): only geometry counts. A wall line on A-WALL is cut at a column on A-COLUMN.\n" +
            "On: layer, color, linetype etc. of the line and the block content must be the same.");

        _openJunctions = AddCheckBox(
            "Open wall junctions (T, +)",
            settings.WallTrim.OpenJunctions,
            "Removes the parts of wall lines that run across another wall, so T and cross junctions\n" +
            "look like the faces of the rooms around them. Wall end lines, door jambs and L corners are not cut.\n" +
            "Two parallel lines on the same layer, at most 'Max wall thickness' apart, are a wall.");

        _createOutlines = AddCheckBox(
            "Create closed polylines",
            settings.WallTrim.CreateClosedOutlines,
            "After trimming, the remaining wall lines become closed polylines (ready for hatching):\n" +
            "the ends of two parallel faces are closed and lines meeting end to end on the same layer are joined.\n" +
            "A wall running into an untrimmed wall face gets its own outline; a trimmed junction becomes one outline.\n" +
            "Lines that do not close are left alone and listed in the results.");

        _maxThickness = AddTextBox(
            "Max wall thickness",
            settings.WallTrim.MaxWallThickness.ToString("G", CultureInfo.InvariantCulture),
            "Largest distance between the two faces of a wall, in drawing units (e.g. 60 for a cm drawing).\n" +
            "The thickness itself is measured from the lines; this only keeps corridors and rooms from being\n" +
            "taken as walls.");

        _wallLayers = AddTextBox(
            "Wall layers",
            LayerPurgeOptions.FormatPatterns(settings.WallTrim.WallLayers),
            "Layers of the wall lines (wildcards * and ?, separated by ;), e.g. DUVAR*; *WALL*\n" +
            "Only these lines are opened at junctions and joined into closed polylines;\n" +
            "window, door and furniture lines on other layers are left alone.\n" +
            "Empty = every layer. The trim at blocks is not affected.");
        _wallLayers.PlaceholderText = "all layers, e.g. DUVAR*; *WALL*";
        _pickLayers = AttachButton(_wallLayers, "...", "Choose layers from a list");
        _pickLayers.Click += (_, _) => PickLayers();

        _keepOriginals = AddCheckBox(
            "Keep original lines",
            settings.WallTrim.KeepOriginalLines,
            "Off (default): lines that became part of a closed polyline are erased, like PEDIT Join.\n" +
            "On: they are kept and the polylines are added on top.");

        _createOutlines.CheckedChanged += (_, _) => UpdateOutlineFields();
        _openJunctions.CheckedChanged += (_, _) => UpdateOutlineFields();
        UpdateOutlineFields();
    }

    private void UpdateOutlineFields()
    {
        _maxThickness.Enabled = _createOutlines.Checked || _openJunctions.Checked;
        _wallLayers.Enabled = _createOutlines.Checked || _openJunctions.Checked;
        _pickLayers.Enabled = _wallLayers.Enabled;
        _keepOriginals.Enabled = _createOutlines.Checked;
    }

    public override bool TryApply(ToolSettings current, out ToolSettings updated, out string? error)
    {
        updated = current;

        var trimInside = LayerPurgeOptions.ParsePatterns(_trimInside.Text);
        if (trimInside.Any(p => SwitchWords.Contains(p, StringComparer.OrdinalIgnoreCase)))
        {
            error = "Trim inside blocks expects block names (e.g. K*; KOLON*), not true/false. Leave empty to turn it off.";
            _trimInside.Focus();
            _trimInside.SelectAll();
            return false;
        }

        if (!TryReadPositive(_maxThickness.Text, out var maxThickness))
        {
            error = "Max wall thickness must be a positive number (drawing units), e.g. 60.";
            _maxThickness.Focus();
            _maxThickness.SelectAll();
            return false;
        }

        updated = current with
        {
            WallTrim = current.WallTrim with
            {
                TrimInsideBlocks = trimInside,
                MatchProperties = _matchProperties.Checked,
                OpenJunctions = _openJunctions.Checked,
                WallLayers = LayerPurgeOptions.ParsePatterns(_wallLayers.Text),
                CreateClosedOutlines = _createOutlines.Checked,
                MaxWallThickness = maxThickness,
                KeepOriginalLines = _keepOriginals.Checked,
            }
        };
        error = null;
        return true;
    }

    /// <summary>Parses a positive number; accepts both "," and "." as the decimal separator.</summary>
    internal static bool TryReadPositive(string? text, out double value) =>
        double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && value > 0 && double.IsFinite(value);

    /// <summary>Block list for "Trim inside blocks"; blocks with a closed outline pass the filter.</summary>
    private void PickBlocks() =>
        PickNames(
            _trimInside,
            "blocks",
            () => _host.GetBlocks().Select(b => new PickerItem(b.Name, b.InSelection, b.HasClosedOutline)).ToList(),
            "Select blocks",
            "Only blocks with a closed outline",
            "No blocks found.");

    /// <summary>Layer list for "Wall layers"; layers with lines or polylines in the selection pass the filter.</summary>
    private void PickLayers() =>
        PickNames(
            _wallLayers,
            "layers",
            () => _host.GetLayers().Select(l => new PickerItem(l.Name, l.InSelection, l.InSelection)).ToList(),
            "Select wall layers",
            "Only layers with lines in the selection",
            "No layers found.");

    /// <summary>
    /// Picking from a list into a pattern field: checked names are written in full; wildcard patterns typed by hand
    /// (like K* or DUVAR*) are kept, and names they already cover are not repeated. Names matching the current
    /// patterns start out checked.
    /// </summary>
    private static void PickNames(
        TextBox box, string what, Func<IReadOnlyList<PickerItem>> load, string title, string filterText, string emptyText)
    {
        IReadOnlyList<PickerItem> items;
        try
        {
            items = load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(box.FindForm(), $"Could not read {what}: " + ex.Message, "CleanTool",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var current = LayerPurgeOptions.ParsePatterns(box.Text);
        var wildcards = current.Where(p => p.Contains('*') || p.Contains('?')).ToList();
        var initiallyChecked = items
            .Where(i => current.Any(p => WildcardMatcher.IsMatch(i.Name, p)))
            .Select(i => i.Name);

        using var picker = new NamePickerForm(title, items, initiallyChecked, filterText, emptyText);
        if (picker.ShowDialog(box.FindForm()) != DialogResult.OK) return;

        var names = picker.SelectedNames
            .Where(n => !wildcards.Any(w => WildcardMatcher.IsMatch(n, w)));

        box.Text = LayerPurgeOptions.FormatPatterns(wildcards.Concat(names));
    }
}

/// <summary>Options of the unused layer purge (layers to keep).</summary>
internal sealed class LayerPurgeOptionsPage : OptionsPageBase
{
    private readonly TextBox _excluded;

    /// <summary>Creates the page from the current settings.</summary>
    public LayerPurgeOptionsPage(ToolSettings settings)
    {
        _excluded = AddTextBox("Keep layers", LayerPurgeOptions.FormatPatterns(settings.Layers.ExcludedPatterns));
    }

    public override bool TryApply(ToolSettings current, out ToolSettings updated, out string? error)
    {
        updated = current with
        {
            Layers = current.Layers with { ExcludedPatterns = LayerPurgeOptions.ParsePatterns(_excluded.Text) }
        };
        error = null;
        return true;
    }
}
