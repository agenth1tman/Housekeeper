using Housekeeper.Ui.Controls;

namespace Housekeeper.Ui;

/// <summary>Layout helpers, so every screen is built from the same few pieces.</summary>
internal static class Make
{
    public static TextLine Title(string text) => new(text)
    {
        Font = AppFonts.Title,
        ForeColor = Theme.Ink,
        Margin = new Padding(0, 0, 0, 2),
    };

    public static TextLine Section(string text) => new(text)
    {
        Font = AppFonts.Section,
        ForeColor = Theme.Ink,
        Margin = new Padding(0, 26, 0, 8),
    };

    public static TextLine Text(string text) => new(text) { Anchor = AnchorStyles.Left };

    /// <summary>Quiet secondary text, optionally lined up under a check box's label.</summary>
    public static TextLine Note(string text, bool indent = false) => new(text)
    {
        Font = AppFonts.Small,
        ForeColor = Theme.Muted,
        Margin = new Padding(indent ? Theme.TickIndent : 0, 0, 0, 8),
    };

    /// <summary>A validation message under a field; hidden until there is something to say.</summary>
    public static TextLine Error(bool indent = false) => new("")
    {
        Font = AppFonts.Small,
        ForeColor = Theme.Bad,
        Margin = new Padding(indent ? Theme.TickIndent : 0, 2, 0, 4),
        Visible = false,
    };

    /// <summary>Controls side by side, vertically centred, e.g. a sentence with a number box in it.</summary>
    public static FlowLayoutPanel Row(params Control[] items) => Row(false, items);

    public static FlowLayoutPanel Row(bool indent, params Control[] items)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(indent ? Theme.TickIndent : 0, 2, 0, 2),
            Padding = Padding.Empty,
            Anchor = AnchorStyles.Left,
        };
        foreach (var item in items)
        {
            // Anchored left only, a control is centred vertically in the row.
            item.Anchor = AnchorStyles.Left;
            row.Controls.Add(item);
        }
        return row;
    }

    /// <summary>One row of columns; the ones marked Fill share the spare width.</summary>
    public static TableLayoutPanel Columns(params (Control Control, bool Fill)[] cells)
    {
        var table = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = cells.Length,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (int i = 0; i < cells.Length; i++)
        {
            var (control, fill) = cells[i];
            table.ColumnStyles.Add(fill ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));
            control.Anchor = fill ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
            table.Controls.Add(control, i, 0);
        }
        return table;
    }
}

/// <summary>A single column of controls, top to bottom, each as wide as the column.</summary>
internal sealed class Stack : TableLayoutPanel
{
    public Stack()
    {
        DoubleBuffered = true;
        ColumnCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Margin = Padding.Empty;
        Padding = Padding.Empty;
    }

    /// <summary>Adds a row. With <paramref name="fill"/> the row takes the height that's left.</summary>
    public T Add<T>(T control, bool fill = false) where T : Control
    {
        RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
        RowCount = RowStyles.Count;
        if (control.Anchor == (AnchorStyles.Top | AnchorStyles.Left))
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        if (fill)
            control.Dock = DockStyle.Fill;
        Controls.Add(control, 0, RowCount - 1);
        return control;
    }

    /// <summary>Empty space that pushes the rows after it to the bottom.</summary>
    public void AddSpace()
    {
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        RowCount = RowStyles.Count;
    }
}
