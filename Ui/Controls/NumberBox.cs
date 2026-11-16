using System.Drawing.Drawing2D;
using System.Globalization;

namespace Housekeeper.Ui.Controls;

/// <summary>
/// A small whole-number input. Typing a valid number applies it straight away; anything else
/// shows <see cref="Error"/> (for a message next to the field) and keeps the last good value.
/// Up and Down step the value.
/// </summary>
internal sealed class NumberBox : Control
{
    private readonly TextBox _box;
    private int _value;
    private int _digitsWidth = -1;
    private string? _error;

    public NumberBox(int minimum, int maximum, int value, string caption)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Minimum = minimum;
        Maximum = maximum;
        _value = Math.Clamp(value, minimum, maximum);
        AutoSize = true;
        Margin = new Padding(8, 0, 8, 0);

        _box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Field,
            ForeColor = Theme.Ink,
            Font = AppFonts.Strong,
            TextAlign = HorizontalAlignment.Center,
            MaxLength = maximum.ToString(CultureInfo.InvariantCulture).Length,
            AccessibleName = caption,
            Text = Show(_value),
        };
        _box.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar))
                e.Handled = true;
        };
        _box.KeyDown += OnBoxKeyDown;
        _box.TextChanged += (_, _) => Parse();
        _box.Enter += (_, _) => Invalidate();
        _box.Leave += (_, _) =>
        {
            // Leaving with a bad entry puts the last good value back.
            if (_error is not null)
            {
                _box.Text = Show(_value);
                SetError(null);
            }
            Invalidate();
        };
        Controls.Add(_box);
    }

    public int Minimum { get; }
    public int Maximum { get; }

    public int Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, Minimum, Maximum);
            _box.Text = Show(value);
            _box.SelectAll();
        }
    }

    /// <summary>Why the current text isn't a usable number, or null.</summary>
    public string? Error => _error;

    public event EventHandler? ValueChanged;
    public event EventHandler? ErrorChanged;

    public void FocusInput() => _box.Focus();

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (_digitsWidth < 0)
            _digitsWidth = TextRenderer.MeasureText(new string('0', Math.Max(3, Maximum.ToString(CultureInfo.InvariantCulture).Length)), _box.Font).Width;
        return new Size(_digitsWidth + LogicalToDeviceUnits(16), LogicalToDeviceUnits(Theme.ControlHeight));
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        _digitsWidth = -1;
        base.OnDpiChangedAfterParent(e);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int pad = LogicalToDeviceUnits(6);
        int height = _box.PreferredHeight;
        _box.SetBounds(pad, (Height - height) / 2, Math.Max(1, Width - 2 * pad), height);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _box.Focus();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Page);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        int radius = LogicalToDeviceUnits(Theme.CornerRadius);
        Drawing.Fill(g, bounds, radius, Theme.Field);
        var border = _error is not null ? Theme.Bad
            : !Enabled ? Theme.Rule
            : _box.Focused ? Theme.Ink
            : Theme.FieldBorder;
        Drawing.Outline(g, bounds, radius, border, LogicalToDeviceUnits(1));
    }

    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        int step = e.KeyCode switch { Keys.Up => 1, Keys.Down => -1, Keys.PageUp => 10, Keys.PageDown => -10, _ => 0 };
        if (step == 0)
            return;
        Value = _value + step;
        e.Handled = e.SuppressKeyPress = true;
    }

    private void Parse()
    {
        if (!int.TryParse(_box.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < Minimum || value > Maximum)
        {
            SetError($"Enter a whole number from {Minimum.ToString("N0", CultureInfo.CurrentCulture)} to {Maximum.ToString("N0", CultureInfo.CurrentCulture)}.");
            return;
        }
        SetError(null);
        if (value == _value)
            return;
        _value = value;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetError(string? error)
    {
        if (_error == error)
            return;
        _error = error;
        _box.AccessibleDescription = error;
        Invalidate();
        ErrorChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Show(int value) => value.ToString(CultureInfo.InvariantCulture);
}
