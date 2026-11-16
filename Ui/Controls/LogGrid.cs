using Housekeeper.Core;

namespace Housekeeper.Ui.Controls;

/// <summary>
/// The cleaning log: a ruled table on the page, one row per file or folder in the temp folder.
/// It runs in virtual mode, so tens of thousands of rows cost nothing until they scroll into view.
/// </summary>
internal sealed class LogGrid : DataGridView
{
    private const int ResultWidth = 150;
    private const int FreedWidth = 110;

    private readonly DataGridViewTextBoxColumn _result, _item, _freed;
    private IReadOnlyList<CleanItem> _items = [];

    public LogGrid()
    {
        DoubleBuffered = true;
        VirtualMode = true;
        ReadOnly = true;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        AllowUserToOrderColumns = false;
        RowHeadersVisible = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        StandardTab = true;
        ShowCellToolTips = true;
        BorderStyle = BorderStyle.None;
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        EnableHeadersVisualStyles = false;
        BackgroundColor = Theme.Page;
        GridColor = Theme.Rule;
        Margin = Padding.Empty;
        AccessibleName = "Cleaning log";

        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Page,
            ForeColor = Theme.Text,
            SelectionBackColor = Theme.Hover,
            SelectionForeColor = Theme.Ink,
            Font = AppFonts.Grid,
            WrapMode = DataGridViewTriState.False,
            Alignment = DataGridViewContentAlignment.MiddleLeft,
        };
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Page,
            ForeColor = Theme.Muted,
            SelectionBackColor = Theme.Page,
            SelectionForeColor = Theme.Muted,
            Font = AppFonts.Small,
            Alignment = DataGridViewContentAlignment.MiddleLeft,
        };

        _result = Column("Result", ResultWidth);
        _item = Column("File or Folder", 0);
        _item.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _freed = Column("Freed", FreedWidth);
        _freed.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _freed.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        Columns.AddRange(_result, _item, _freed);
    }

    /// <summary>Shown in the empty table: what's missing and what to do about it.</summary>
    public string EmptyText { get; set; } = "";

    public static string Describe(CleanOutcome outcome) => outcome switch
    {
        CleanOutcome.Deleted => "Deleted",
        CleanOutcome.Partly => "Partly deleted",
        CleanOutcome.InUse => "In use, kept",
        CleanOutcome.NoAccess => "No access",
        _ => "Recent, kept",
    };

    /// <summary>Shows <paramref name="items"/>. Call <see cref="Sync"/> when the list grows.</summary>
    public void Bind(IReadOnlyList<CleanItem> items)
    {
        _items = items;
        Sync();
        Invalidate();
    }

    /// <summary>Catches up with new rows, following the end of the log if it was already there.</summary>
    public void Sync()
    {
        int count = _items.Count;
        if (count == RowCount)
            return;
        bool following = RowCount == 0 || FirstDisplayedScrollingRowIndex + DisplayedRowCount(true) >= RowCount;
        RowCount = count;
        if (following && count > 0 && IsHandleCreated)
            FirstDisplayedScrollingRowIndex = Math.Max(0, count - DisplayedRowCount(false));
        Invalidate();
    }

    protected override void OnCellValueNeeded(DataGridViewCellValueEventArgs e)
    {
        base.OnCellValueNeeded(e);
        if (e.RowIndex >= _items.Count)
            return;
        var item = _items[e.RowIndex];
        e.Value = e.ColumnIndex switch
        {
            0 => Describe(item.Outcome),
            1 => item.DisplayName,
            _ => item.BytesFreed > 0 ? Format.Bytes(item.BytesFreed) : "–",
        };
    }

    protected override void OnCellFormatting(DataGridViewCellFormattingEventArgs e)
    {
        base.OnCellFormatting(e);
        if (e.RowIndex < 0 || e.RowIndex >= _items.Count || e.CellStyle is null)
            return;
        var item = _items[e.RowIndex];
        if (e.ColumnIndex == 0)
        {
            var tint = item.Outcome switch
            {
                CleanOutcome.Deleted => Theme.Good,
                CleanOutcome.Partly => Theme.Caution,
                CleanOutcome.Recent => Theme.Muted,
                _ => Theme.Bad,
            };
            e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = tint;
        }
        else if (e.ColumnIndex == 2 && item.BytesFreed == 0)
        {
            e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Theme.Muted;
        }
    }

    protected override void OnCellToolTipTextNeeded(DataGridViewCellToolTipTextNeededEventArgs e)
    {
        base.OnCellToolTipTextNeeded(e);
        if (e.RowIndex < 0 || e.RowIndex >= _items.Count || e.ColumnIndex != 0)
            return;
        var item = _items[e.RowIndex];
        if (item.ItemsLeft > 0)
            e.ToolTipText = $"{Format.Count(item.FilesDeleted, "file", "files")} deleted, {Format.Count(item.ItemsLeft, "item", "items")} kept";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var pen = new Pen(Theme.Rule))
            e.Graphics.DrawLine(pen, 0, ColumnHeadersHeight - 1, Width, ColumnHeadersHeight - 1);
        if (RowCount == 0 && EmptyText.Length > 0)
        {
            var area = Rectangle.FromLTRB(0, ColumnHeadersHeight + LogicalToDeviceUnits(16), Width, Height);
            TextRenderer.DrawText(e.Graphics, EmptyText, AppFonts.Body, area, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyScale();
        Native.UseDarkScrollBars(VerticalScrollBar);
        Native.UseDarkScrollBars(HorizontalScrollBar);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyScale();
    }

    // Row height and column widths are pixels the framework doesn't scale by itself.
    private void ApplyScale()
    {
        int rows = RowCount;
        RowCount = 0;
        RowTemplate.Height = LogicalToDeviceUnits(Theme.RowHeight);
        ColumnHeadersHeight = LogicalToDeviceUnits(Theme.RowHeight);
        _result.Width = LogicalToDeviceUnits(ResultWidth);
        _freed.Width = LogicalToDeviceUnits(FreedWidth);
        RowCount = rows;
    }

    private static DataGridViewTextBoxColumn Column(string header, int width)
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Resizable = DataGridViewTriState.False,
            ReadOnly = true,
        };
        if (width > 0)
            column.Width = width;
        return column;
    }
}

/// <summary>Colours the tray menu like the rest of the app.</summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColors()) => RoundedEdges = false;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Ink : Theme.Faint;
        base.OnRenderItemText(e);
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Sidebar;
        public override Color ImageMarginGradientBegin => Theme.Sidebar;
        public override Color ImageMarginGradientMiddle => Theme.Sidebar;
        public override Color ImageMarginGradientEnd => Theme.Sidebar;
        public override Color MenuBorder => Theme.Rule;
        public override Color MenuItemBorder => Theme.Hover;
        public override Color MenuItemSelected => Theme.Hover;
        public override Color MenuItemSelectedGradientBegin => Theme.Hover;
        public override Color MenuItemSelectedGradientEnd => Theme.Hover;
        public override Color MenuItemPressedGradientBegin => Theme.Pressed;
        public override Color MenuItemPressedGradientEnd => Theme.Pressed;
        public override Color SeparatorDark => Theme.Rule;
        public override Color SeparatorLight => Theme.Sidebar;
    }
}
