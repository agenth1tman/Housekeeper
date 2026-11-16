using System.Drawing.Drawing2D;

namespace Housekeeper.Ui.Controls;

/// <summary>A check box drawn to match the dark page. Still a real CheckBox underneath.</summary>
internal sealed class ToggleBox : CheckBox
{
    private bool _hover;
    private Size _measured;

    public ToggleBox(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        UseMnemonic = false;
        Font = AppFonts.Body;
        ForeColor = Theme.Text;
        AutoSize = true;
        Margin = new Padding(0, 4, 0, 2);
    }

    public override Size GetPreferredSize(Size proposedSize) => _measured.IsEmpty ? _measured = Tick.PreferredSize(this) : _measured;

    protected override void OnTextChanged(EventArgs e) { _measured = Size.Empty; base.OnTextChanged(e); }

    protected override void OnFontChanged(EventArgs e) { _measured = Size.Empty; base.OnFontChanged(e); }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }

    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e) =>
        Tick.Paint(this, e.Graphics, Checked, _hover, Focused && ShowFocusCues, round: false);
}

/// <summary>An option button (one of a group) drawn to match the dark page. Still a real RadioButton.</summary>
internal sealed class ChoiceBox : RadioButton
{
    private bool _hover;
    private Size _measured;

    public ChoiceBox(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        UseMnemonic = false;
        Font = AppFonts.Body;
        ForeColor = Theme.Text;
        AutoSize = true;
        Margin = new Padding(0, 4, 24, 2);
    }

    public override Size GetPreferredSize(Size proposedSize) => _measured.IsEmpty ? _measured = Tick.PreferredSize(this) : _measured;

    protected override void OnTextChanged(EventArgs e) { _measured = Size.Empty; base.OnTextChanged(e); }

    protected override void OnFontChanged(EventArgs e) { _measured = Size.Empty; base.OnFontChanged(e); }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }

    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e) =>
        Tick.Paint(this, e.Graphics, Checked, _hover, Focused && ShowFocusCues, round: true);
}

/// <summary>Shared painting for <see cref="ToggleBox"/> and <see cref="ChoiceBox"/>.</summary>
internal static class Tick
{
    private const int Box = 18;
    private const TextFormatFlags Flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    public static Size PreferredSize(Control c)
    {
        var text = TextRenderer.MeasureText(c.Text, c.Font, Size.Empty, Flags);
        return new Size(
            c.LogicalToDeviceUnits(Theme.TickIndent) + text.Width + c.LogicalToDeviceUnits(4),
            Math.Max(c.LogicalToDeviceUnits(Box + 8), text.Height + c.LogicalToDeviceUnits(8)));
    }

    public static void Paint(Control c, Graphics g, bool on, bool hover, bool focus, bool round)
    {
        g.Clear(c.BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int size = c.LogicalToDeviceUnits(Box);
        var box = new Rectangle(c.LogicalToDeviceUnits(2), (c.Height - size) / 2, size - 1, size - 1);
        float stroke = c.LogicalToDeviceUnits(1) * 1.5f;
        var border = !c.Enabled ? Theme.Faint : hover ? Theme.Ink : Theme.Muted;
        var mark = c.Enabled ? Theme.Accent : Theme.Faint;

        if (round)
        {
            using (var fill = new SolidBrush(Theme.Field))
                g.FillEllipse(fill, box);
            using (var pen = new Pen(on ? mark : border, stroke))
                g.DrawEllipse(pen, box);
            if (on)
            {
                var dot = Rectangle.Inflate(box, -size / 4, -size / 4);
                using var brush = new SolidBrush(mark);
                g.FillEllipse(brush, dot);
            }
        }
        else
        {
            int radius = c.LogicalToDeviceUnits(Theme.CornerRadius) - 1;
            if (on)
            {
                Drawing.Fill(g, box, radius, mark);
                using var pen = new Pen(Theme.OnAccent, c.LogicalToDeviceUnits(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(pen, new[]
                {
                    new PointF(box.X + box.Width * 0.24f, box.Y + box.Height * 0.52f),
                    new PointF(box.X + box.Width * 0.43f, box.Y + box.Height * 0.71f),
                    new PointF(box.X + box.Width * 0.77f, box.Y + box.Height * 0.31f),
                });
            }
            else
            {
                Drawing.Fill(g, box, radius, Theme.Field);
                Drawing.Outline(g, box, radius, border, stroke);
            }
        }

        if (focus)
        {
            int gap = c.LogicalToDeviceUnits(3);
            var ring = Rectangle.Inflate(box, gap, gap);
            using var pen = new Pen(Theme.Ink, c.LogicalToDeviceUnits(1));
            if (round)
                g.DrawEllipse(pen, ring);
            else
                Drawing.Outline(g, ring, c.LogicalToDeviceUnits(Theme.CornerRadius) + gap, Theme.Ink, c.LogicalToDeviceUnits(1));
        }

        g.SmoothingMode = SmoothingMode.None;
        int left = c.LogicalToDeviceUnits(Theme.TickIndent);
        TextRenderer.DrawText(g, c.Text, c.Font, Rectangle.FromLTRB(left, 0, c.Width, c.Height),
            c.Enabled ? c.ForeColor : Theme.Faint, Flags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
