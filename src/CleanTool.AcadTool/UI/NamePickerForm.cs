using System.Drawing;
using System.Windows.Forms;

namespace CleanTool.AcadTool.UI;

/// <summary>One row of <see cref="NamePickerForm"/>.</summary>
/// <param name="Name">Block or layer name.</param>
/// <param name="InSelection">Shown first and marked "(in selection)".</param>
/// <param name="PassesFilter">Shown while the optional filter check box is on.</param>
internal sealed record PickerItem(string Name, bool InSelection, bool PassesFilter);

/// <summary>
/// Name picker dialog (blocks for "Trim inside blocks", layers for "Wall layers"). Same compact light style as the
/// main dialog.
///
///   Select blocks
///   ┌───────────────────────────────┐
///   │ [x] K1          (in selection)│
///   │ [x] K2          (in selection)│
///   │ [ ] KOLON-YUVARLAK            │
///   └───────────────────────────────┘
///   [x] Only blocks with a closed outline
///   [All] [None]            [OK] [Cancel]
///
/// Items in the selection are listed first. It does not know AutoCAD; the list is passed in.
/// </summary>
internal sealed class NamePickerForm : Form
{
    private readonly IReadOnlyList<PickerItem> _items;
    private readonly HashSet<string> _checked;

    private readonly CheckedListBox _list = new()
    {
        Dock = DockStyle.Fill,
        CheckOnClick = true,
        IntegralHeight = false,
        BorderStyle = BorderStyle.FixedSingle
    };

    private readonly CheckBox _filter = new()
    {
        Checked = true,
        AutoSize = true,
        Margin = new Padding(0, 6, 0, 0)
    };

    private readonly Label _empty = new()
    {
        AutoSize = true,
        Visible = false,
        Margin = new Padding(0, 4, 0, 0)
    };

    /// <summary>Creates the picker.</summary>
    /// <param name="title">Window title, e.g. "Select blocks".</param>
    /// <param name="items">Names to list.</param>
    /// <param name="initiallyChecked">Names that start out checked.</param>
    /// <param name="filterText">Text of the filter check box (e.g. "Only blocks with a closed outline"); null for no filter.</param>
    /// <param name="emptyText">Shown when nothing is listed, e.g. "No blocks found."</param>
    public NamePickerForm(
        string title, IReadOnlyList<PickerItem> items, IEnumerable<string> initiallyChecked, string? filterText, string emptyText)
    {
        _items = items
            .OrderByDescending(i => i.InSelection)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _checked = new HashSet<string>(initiallyChecked, StringComparer.OrdinalIgnoreCase);

        _filter.Text = filterText ?? string.Empty;
        _filter.Visible = filterText is not null;
        _empty.Text = emptyText;

        InitializeLayout();
        Text = title;

        // Without a filter, or when nothing passes it, show everything from the start.
        if (filterText is null || !_items.Any(i => i.PassesFilter)) _filter.Checked = false;

        _filter.CheckedChanged += (_, _) => Populate();
        _list.ItemCheck += (_, e) =>
        {
            var name = ((Item)_list.Items[e.Index]).Value.Name;
            if (e.NewValue == CheckState.Checked) _checked.Add(name);
            else _checked.Remove(name);
        };

        Populate();
    }

    /// <summary>After OK: the checked names (including hidden ones), sorted by name.</summary>
    public IReadOnlyList<string> SelectedNames =>
        _checked.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    private void InitializeLayout()
    {
        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(320, 360);
        Padding = new Padding(12);

        // Small fixed-width buttons so that all four fit in the narrow window.
        var ok = CreateButton("OK", 64, new Padding(6, 0, 0, 0));
        var cancel = CreateButton("Cancel", 64, new Padding(6, 0, 0, 0));
        var all = CreateButton("All", 48, new Padding(0, 0, 6, 0));
        var none = CreateButton("None", 48, Padding.Empty);
        ok.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = ok;
        CancelButton = cancel;

        all.Click += (_, _) => SetAllVisible(true);
        none.Click += (_, _) => SetAllVisible(false);

        var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        left.Controls.AddRange(new Control[] { all, none });

        var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        right.Controls.AddRange(new Control[] { cancel, ok });

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.Controls.Add(left, 0, 0);
        buttons.Controls.Add(right, 1, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_list, 0, 0);
        root.Controls.Add(_empty, 0, 1);
        root.Controls.Add(_filter, 0, 2);
        root.Controls.Add(buttons, 0, 3);

        Controls.Add(root);
        ResumeLayout(true);
    }

    private void Populate()
    {
        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var item in _items.Where(i => !_filter.Checked || i.PassesFilter))
            _list.Items.Add(new Item(item), _checked.Contains(item.Name));

        _list.EndUpdate();
        _empty.Visible = _list.Items.Count == 0;
    }

    private void SetAllVisible(bool value)
    {
        for (var i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, value); // ItemCheck updates _checked
    }

    private static Button CreateButton(string text, int width, Padding margin) => new()
    {
        Text = text,
        AutoSize = false,
        Size = new Size(width, 26),
        Margin = margin
    };

    private sealed record Item(PickerItem Value)
    {
        public override string ToString() => Value.InSelection ? $"{Value.Name}   (in selection)" : Value.Name;
    }
}
