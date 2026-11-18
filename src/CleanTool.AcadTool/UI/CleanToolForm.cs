using System.Drawing;
using System.Windows.Forms;
using CleanTool.AcadTool.Composition;
using CleanTool.AcadTool.Infrastructure;
using CleanTool.Core.Pipeline;

namespace CleanTool.AcadTool.UI;

/// <summary>Per-tool customization of the shared dialog.</summary>
/// <param name="Title">Window title.</param>
/// <param name="ShowIncludeBlocks">Show the "Include blocks" row? (for tools that clean inside blocks)</param>
internal sealed record ToolFormOptions(string Title, bool ShowIncludeBlocks)
{
    /// <summary>Options of the Clean tool's dialog.</summary>
    public static ToolFormOptions Clean { get; } = new("CleanTool", ShowIncludeBlocks: true);
}

/// <summary>
/// The shared tool dialog — compact and single-column, like OVERKILL:
///
///   [Select objects]  24 objects selected
///   Include blocks  [No              v]
///   ┌ Operations ───────────────────────┐
///   │ [x] Flatten (Z = 0)               │
///   │       Z tolerance [        ]      │
///   │ [x] Duplicate objects             │
///   │       ...                         │
///   └───────────────────────────────────┘
///   Results
///   [                                   ]
///   status          [Preview] [Run] [Close]
///
/// The operation sections are built from the registry, so this file does not change when an operation is added.
/// </summary>
internal sealed class CleanToolForm : Form
{
    private const int FormWidth = 400;

    private readonly SettingsStore _store;
    private readonly ICleanSession _session;
    private readonly IReadOnlyList<OperationSection> _sections;
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 15000 };

    private readonly Button _selectButton = CreateButton("Select objects");
    private readonly Label _selectionLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 0, 0, 0) };
    private readonly ComboBox _blockMode = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Margin = new Padding(0, 2, 0, 2)
    };

    private readonly TextBox _results = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        BackColor = Color.White,
        Height = 130,
        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        Margin = new Padding(0, 2, 0, 0)
    };

    private readonly Label _status = new() { AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(170, 0) };
    private readonly Button _previewButton = CreateButton("Preview");
    private readonly Button _runButton = CreateButton("Run");
    private readonly Button _closeButton = CreateButton("Close");

    private TableLayoutPanel _root = null!;

    private readonly ToolFormOptions _options;

    /// <summary>Creates the dialog.</summary>
    /// <param name="factories">The tool's operations; one section each.</param>
    /// <param name="store">The tool's settings; updated on Preview/Run.</param>
    /// <param name="session">Selection and execution in AutoCAD.</param>
    /// <param name="options">Title and optional rows; the Clean layout when null.</param>
    public CleanToolForm(IEnumerable<IOperationFactory> factories, SettingsStore store, ICleanSession session, ToolFormOptions? options = null)
    {
        _options = options ?? ToolFormOptions.Clean;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _session = session ?? throw new ArgumentNullException(nameof(session));

        _sections = factories
            .Select(f => new OperationSection(f, f.CreateOptionsPage(store.Current, session), store.IsEnabled(f.Id)))
            .ToList();

        _blockMode.Items.AddRange(new object[]
        {
            new BlockModeItem(BlockMode.None, "No"),
            new BlockModeItem(BlockMode.SelectedBlocks, "Selected blocks"),
            new BlockModeItem(BlockMode.AllBlocks, "All blocks in drawing")
        });
        _blockMode.SelectedItem = _blockMode.Items.Cast<BlockModeItem>().First(i => i.Mode == store.Current.Blocks);

        InitializeLayout();

        _selectButton.Click += (_, _) => SelectObjects();
        _previewButton.Click += (_, _) => Execute(dryRun: true);
        _runButton.Click += (_, _) => Execute(dryRun: false);
        _closeButton.DialogResult = DialogResult.Cancel;

        UpdateSelectionLabel();
        SetStatus(_session.HasSelection ? "Ready." : "Select objects to start.");
    }

    // ------------------------------------------------------------------ layout

    private void InitializeLayout()
    {
        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = _options.Title;
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(12);
        ClientSize = new Size(FormWidth, 400); // The height is fitted to the content in OnLoad.
        CancelButton = _closeButton;

        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(CreateSelectionRow());
        if (_options.ShowIncludeBlocks) AddRow(CreateBlockRow());
        AddRow(CreateOperationsGroup());
        AddRow(new Label { Text = "Results", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        AddRow(_results);
        AddRow(CreateFooter());

        Controls.Add(_root);
        ResumeLayout(true);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Like OVERKILL: exactly as tall as the content, no more.
        ClientSize = new Size(ClientSize.Width, _root.PreferredSize.Height + Padding.Vertical);
    }

    private Control CreateSelectionRow()
    {
        var row = CreateRowPanel(2);
        row.Controls.Add(_selectButton, 0, 0);
        row.Controls.Add(_selectionLabel, 1, 0);
        return row;
    }

    private Control CreateBlockRow()
    {
        var row = CreateRowPanel(2, stretchLast: true);
        row.Margin = new Padding(0, 8, 0, 0);
        var label = new Label { Text = "Include blocks", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 10, 0) };
        row.Controls.Add(label, 0, 0);
        row.Controls.Add(_blockMode, 1, 0);

        const string tip = "Also cleans inside block definitions (including nested and dynamic blocks).\n" +
                           "Changing a definition changes every insert of that block.\n" +
                           "Duplicates are not removed inside dynamic blocks (visibility states reuse geometry).\n" +
                           "Xref and other anonymous blocks are skipped. This takes longer on large drawings.";
        _toolTip.SetToolTip(label, tip);
        _toolTip.SetToolTip(_blockMode, tip);
        return row;
    }

    private Control CreateOperationsGroup()
    {
        var group = new GroupBox
        {
            Text = "Operations",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Padding = new Padding(8, 4, 8, 4),
            Margin = new Padding(0, 8, 0, 0)
        };

        var list = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        foreach (var section in _sections)
        {
            _toolTip.SetToolTip(section.Enabled, section.Factory.Description);
            AddTo(list, section.Enabled);
            if (section.Options is not null) AddTo(list, section.Options.View);
        }

        group.Controls.Add(list);
        return group;
    }

    private Control CreateFooter()
    {
        var footer = CreateRowPanel(2);
        footer.Margin = new Padding(0, 10, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty
        };
        buttons.Controls.AddRange(new Control[] { _closeButton, _runButton, _previewButton });

        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(buttons, 1, 0);
        // The status text takes the remaining width; the buttons align right.
        footer.ColumnStyles[0].SizeType = SizeType.Percent;
        footer.ColumnStyles[0].Width = 100;
        return footer;
    }

    private void AddRow(Control control) => AddTo(_root, control);

    private static void AddTo(TableLayoutPanel panel, Control control)
    {
        var row = panel.RowCount;
        panel.RowCount = row + 1;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (control.Anchor == (AnchorStyles.Top | AnchorStyles.Left))
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        panel.Controls.Add(control, 0, row);
    }

    /// <summary>Single-row horizontal layout. stretchLast: the last column takes the remaining width.</summary>
    private static TableLayoutPanel CreateRowPanel(int columns, bool stretchLast = false)
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = columns,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Margin = Padding.Empty
        };
        for (var c = 0; c < columns; c++)
        {
            var isLast = c == columns - 1;
            panel.ColumnStyles.Add(isLast && stretchLast ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));
        }
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return panel;
    }

    // ------------------------------------------------------------------ behaviour

    private void SelectObjects()
    {
        if (_session.SelectObjects(Handle))
        {
            UpdateSelectionLabel();
            SetStatus("Ready.");
        }
    }

    private void UpdateSelectionLabel() => _selectionLabel.Text = _session.SelectionDescription;

    private void Execute(bool dryRun)
    {
        var selected = _sections.Where(s => s.Enabled.Checked).ToList();
        if (selected.Count == 0)
        {
            SetStatus("Select at least one operation.");
            return;
        }

        if (!_session.HasSelection && selected.Any(s => s.Factory.UsesSelection))
        {
            SetStatus("Select objects first.");
            return;
        }

        if (!TryCollectSettings(out var settings, out var error))
        {
            SetStatus(error);
            return;
        }

        var ids = selected.Select(s => s.Factory.Id).ToList();
        _store.Update(settings, ids);

        SetBusy(true);
        try
        {
            var report = _session.Run(ids, dryRun, settings);
            _results.Text = string.Join(Environment.NewLine, CleanReportFormatter.Format(report));

            SetStatus(dryRun
                ? "Preview only. Nothing changed."
                : report.HasFailures
                    ? $"Done with errors. {report.TotalChanged} change(s)."
                    : $"Done. {report.TotalChanged} change(s).");
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Applies the general settings and every options page; stops at the first invalid field.</summary>
    private bool TryCollectSettings(out ToolSettings settings, out string error)
    {
        var mode = ((BlockModeItem)_blockMode.SelectedItem!).Mode;
        settings = _store.Current with { Blocks = mode };

        foreach (var section in _sections)
        {
            if (section.Options is null) continue;
            if (section.Options.TryApply(settings, out settings, out var pageError)) continue;

            error = pageError ?? "Invalid value.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _selectButton.Enabled = _previewButton.Enabled = _runButton.Enabled = _closeButton.Enabled = !busy;
        if (busy) SetStatus("Working...");
        Update();
    }

    private void SetStatus(string text) => _status.Text = text;

    private static Button CreateButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(75, 26),
        Margin = new Padding(6, 0, 0, 0)
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTip.Dispose();
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ types

    /// <summary>An operation's checkbox and (if any) its options below it.</summary>
    private sealed class OperationSection
    {
        /// <summary>Creates the section for one operation.</summary>
        public OperationSection(IOperationFactory factory, IOptionsPage? options, bool enabled)
        {
            Factory = factory;
            Options = options;
            Enabled = new CheckBox
            {
                Text = factory.DisplayName,
                Checked = enabled,
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 2)
            };

            // Options of a disabled operation are greyed out.
            if (options is not null)
            {
                var view = options.View;
                var box = Enabled;
                view.Enabled = enabled;
                box.CheckedChanged += (_, _) => view.Enabled = box.Checked;
            }
        }

        /// <summary>The operation's factory.</summary>
        public IOperationFactory Factory { get; }
        /// <summary>The operation's options page, if it has one.</summary>
        public IOptionsPage? Options { get; }
        /// <summary>The checkbox that turns the operation on or off.</summary>
        public CheckBox Enabled { get; }
    }

    private sealed record BlockModeItem(BlockMode Mode, string Text)
    {
        public override string ToString() => Text;
    }
}
