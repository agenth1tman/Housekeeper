namespace Housekeeper.Ui.Controls;

/// <summary>
/// One line of text. It never wraps or spills over: when it doesn't fit it ends in "…" and shows
/// the whole text as a tooltip. Changes are announced to screen readers, so it also serves as the
/// live status line next to buttons.
/// </summary>
internal sealed class TextLine : Label
{
    private const TextFormatFlags Flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
    private static readonly ToolTip Tip = new();
    private Size _measured;
    private int _textWidth = -1;
    private string? _reserve;
    private bool _truncated;

    public TextLine(string text)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        UseMnemonic = false;
        AutoSize = true;
        Font = AppFonts.Body;
        ForeColor = Theme.Text;
        Margin = new Padding(0, 2, 0, 2);
        Text = text;
    }

    public bool AlignRight { get; init; }

    /// <summary>
    /// Text to keep room for, e.g. the widest value a live reading can show, so the line keeps
    /// its width as the text changes and nothing next to it moves.
    /// </summary>
    public string? ReserveText
    {
        get => _reserve;
        set
        {
            _reserve = value;
            _measured = Size.Empty;
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        // Measured once per text or font change; layout asks for this many times per pass.
        if (_measured.IsEmpty)
        {
            var text = TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, Font, Size.Empty, Flags);
            _textWidth = Text.Length == 0 ? 0 : text.Width;
            int width = _textWidth;
            if (_reserve is not null)
                width = Math.Max(width, TextRenderer.MeasureText(_reserve, Font, Size.Empty, Flags).Width);
            _measured = new Size(width, text.Height);
        }
        return _measured;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        _measured = Size.Empty;
        _textWidth = -1;
        base.OnTextChanged(e);
        if (IsHandleCreated)
            AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        UpdateTip();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _measured = Size.Empty;
        _textWidth = -1;
        base.OnFontChanged(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateTip();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var flags = Flags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | (AlignRight ? TextFormatFlags.Right : TextFormatFlags.Left);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.Faint, BackColor, flags);
    }

    private void UpdateTip()
    {
        if (_textWidth < 0)
            GetPreferredSize(Size.Empty);
        bool truncated = Width > 0 && _textWidth > Width;
        if (truncated == _truncated && !truncated)
            return;
        _truncated = truncated;
        Tip.SetToolTip(this, truncated ? Text : null);
    }
}
