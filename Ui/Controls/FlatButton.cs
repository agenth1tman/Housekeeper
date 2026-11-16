using System.Drawing.Drawing2D;

namespace Housekeeper.Ui.Controls;

internal enum ButtonKind { Secondary, Primary, Nav }

/// <summary>
/// A flat button in the app's colours. It is still a real <see cref="Button"/>, so Tab, Enter,
/// Space and screen readers work as usual; only the painting is custom. Silver (Primary) is for
/// the one main action of a screen.
/// </summary>
internal sealed class FlatButton : Button
{
    private bool _hover, _pressed, _selected;
    private string[] _reserve = [];
    private int _textWidth = -1;

    public FlatButton(string text, ButtonKind kind = ButtonKind.Secondary)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Kind = kind;
        Text = text;
        UseMnemonic = false;
        UseVisualStyleBackColor = false;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = kind == ButtonKind.Nav ? AppFonts.Nav : AppFonts.Button;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = kind == ButtonKind.Nav ? new Padding(0, 0, 0, 4) : new Padding(0, 0, 12, 0);
    }

    public ButtonKind Kind { get; }

    /// <summary>For navigation buttons: whether this one's page is on screen.</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            AccessibleDescription = value ? "Current page" : null;
            Invalidate();
        }
    }

    /// <summary>
    /// Other labels the button switches to (e.g. "Stop"), so it keeps one width and the text
    /// beside it doesn't jump when the label changes.
    /// </summary>
    public string[] ReserveTexts
    {
        get => _reserve;
        set
        {
            _reserve = value;
            _textWidth = -1;
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        // Measured once per text or font change; layout asks for this many times per pass.
        if (_textWidth < 0)
            _textWidth = _reserve.Append(Text).Max(t => TextRenderer.MeasureText(t, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width);
        int height = LogicalToDeviceUnits(Kind == ButtonKind.Nav ? Theme.NavHeight : Theme.ControlHeight);
        return new Size(_textWidth + LogicalToDeviceUnits(Kind == ButtonKind.Nav ? 28 : 40), height);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        _textWidth = -1;
        base.OnTextChanged(e);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _textWidth = -1;
        base.OnFontChanged(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _hover = _pressed = false;
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var (fill, ink) = Colors();
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        int radius = LogicalToDeviceUnits(Theme.CornerRadius);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (fill.A > 0)
            Drawing.Fill(g, bounds, radius, fill);
        if (Focused && ShowFocusCues)
        {
            int inset = LogicalToDeviceUnits(3);
            Drawing.Outline(g, Rectangle.Inflate(bounds, -inset, -inset), Math.Max(1, radius - 1), ink, LogicalToDeviceUnits(1));
        }
        g.SmoothingMode = SmoothingMode.None;

        var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
        Rectangle textBounds;
        if (Kind == ButtonKind.Nav)
        {
            flags |= TextFormatFlags.Left;
            textBounds = Rectangle.FromLTRB(LogicalToDeviceUnits(14), 0, Width - LogicalToDeviceUnits(8), Height);
        }
        else
        {
            flags |= TextFormatFlags.HorizontalCenter;
            textBounds = ClientRectangle;
        }
        TextRenderer.DrawText(g, Text, Font, textBounds, ink, flags);
    }

    private (Color Fill, Color Ink) Colors()
    {
        if (!Enabled)
            return Kind == ButtonKind.Nav ? (Color.Empty, Theme.Faint) : (Theme.ButtonDisabled, Theme.Faint);
        return Kind switch
        {
            ButtonKind.Primary => (_pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent, Theme.OnAccent),
            ButtonKind.Nav when _selected => (_hover ? Theme.AccentHover : Theme.Accent, Theme.OnAccent),
            ButtonKind.Nav => (_pressed ? Theme.Pressed : _hover ? Theme.Hover : Color.Empty, _hover ? Theme.Ink : Theme.Text),
            _ => (_pressed ? Theme.Pressed : _hover ? Theme.Hover : Theme.ButtonFill, Theme.Ink),
        };
    }
}
