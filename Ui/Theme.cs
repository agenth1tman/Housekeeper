using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Housekeeper.Ui;

/// <summary>
/// Colours and sizes for the whole app; nothing outside this file picks its own. The look is the
/// original one: a near-black page, a dark grey sidebar, white Montserrat type, and silver for the
/// one thing that is selected or primary. Green, amber and red appear only on status words.
/// </summary>
internal static class Theme
{
    public static readonly Color Page = Color.FromArgb(12, 12, 12);
    public static readonly Color Sidebar = Color.FromArgb(21, 21, 21);
    public static readonly Color Field = Color.FromArgb(25, 25, 25);
    public static readonly Color Rule = Color.FromArgb(38, 38, 38);
    public static readonly Color FieldBorder = Color.FromArgb(70, 70, 70);

    public static readonly Color ButtonFill = Color.FromArgb(40, 40, 40);
    public static readonly Color ButtonDisabled = Color.FromArgb(26, 26, 26);
    public static readonly Color Hover = Color.FromArgb(51, 51, 51);
    public static readonly Color Pressed = Color.FromArgb(68, 68, 68);

    public static readonly Color Accent = Color.Silver;
    public static readonly Color AccentHover = Color.FromArgb(216, 216, 216);
    public static readonly Color AccentPressed = Color.FromArgb(166, 166, 166);
    public static readonly Color OnAccent = Color.Black;

    public static readonly Color Ink = Color.White;
    public static readonly Color Text = Color.FromArgb(228, 228, 228);
    public static readonly Color Muted = Color.FromArgb(150, 150, 150);
    public static readonly Color Faint = Color.FromArgb(96, 96, 96);

    public static readonly Color Good = Color.FromArgb(98, 204, 134);
    public static readonly Color Caution = Color.FromArgb(232, 186, 88);
    public static readonly Color Bad = Color.FromArgb(240, 106, 106);

    /// <summary>Corner radius for buttons and inputs, the only rounded things in the app.</summary>
    public const int CornerRadius = 4;
    public const int ControlHeight = 34;
    public const int NavHeight = 40;
    public const int RowHeight = 30;
    public const int SidebarWidth = 216;
    public const int Gutter = 36;

    /// <summary>Indent that lines text up with the label of a check box or option above it.</summary>
    public const int TickIndent = 28;
}

/// <summary>
/// The app's type: Montserrat (SIL Open Font License 1.1), built into the exe so it can't go
/// missing. Hierarchy comes from size and weight: Black for titles, SemiBold/Bold for emphasis,
/// Medium for everything people read.
/// </summary>
/// <remarks>
/// The embedded copies are renamed inside the files to "HK Montserrat …". Windows looks fonts up by
/// name, and with a Montserrat installed on the PC it would sometimes pick that one instead, at a
/// different size or weight.
///
/// They are unpacked to the data folder and loaded from there rather than from memory: loaded
/// from memory, GDI+ files SemiBold under the regular family and the weight is lost.
/// </remarks>
internal static class AppFonts
{
    private static readonly PrivateFontCollection Collection = new();
    private static readonly FontFamily[] Families = Load();

    public static readonly Font Brand = Create("HK Montserrat Black", 13f);
    public static readonly Font Title = Create("HK Montserrat Black", 20f);
    public static readonly Font Figure = Create("HK Montserrat Black", 28f);
    public static readonly Font Section = Create("HK Montserrat Black", 11f);
    public static readonly Font Nav = Create("HK Montserrat", 10.5f, FontStyle.Bold);
    public static readonly Font Button = Create("HK Montserrat SemiBold", 10f);
    public static readonly Font Strong = Create("HK Montserrat SemiBold", 10f);
    public static readonly Font Body = Create("HK Montserrat Medium", 10f);
    public static readonly Font Grid = Create("HK Montserrat Medium", 9.5f);
    public static readonly Font Small = Create("HK Montserrat Medium", 9f);

    private static FontFamily[] Load()
    {
        try
        {
            var assembly = typeof(AppFonts).Assembly;
            var folder = Path.Combine(Core.AppPaths.DataDirectory, "Fonts");
            Directory.CreateDirectory(folder);
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Fonts.", StringComparison.Ordinal)))
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                var data = new byte[stream.Length];
                stream.ReadExactly(data);
                var path = Path.Combine(folder, name["Fonts.".Length..]);
                if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(data))
                    File.WriteAllBytes(path, data);
                // Also registers the font with GDI, which text boxes and TextRenderer draw with.
                Collection.AddFontFile(path);
            }
            return Collection.Families;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or ArgumentException)
        {
            Core.ActivityLog.Error("Couldn't load the built-in fonts.", ex);
            return [];
        }
    }

    private static Font Create(string family, float size, FontStyle style = FontStyle.Regular)
    {
        var match = Array.Find(Families, f => f.Name == family);
        return match is null
            ? new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point)
            : new Font(match, size, style, GraphicsUnit.Point);
    }
}

internal static class AppIcon
{
    public static readonly Icon Window = Load();

    public static Icon Tray() => new(Window, SystemInformation.SmallIconSize);

    public static Bitmap Image(int size)
    {
        using var icon = new Icon(Window, size, size);
        return icon.ToBitmap();
    }

    private static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("Housekeeper.ico");
        return stream is null ? (Icon)SystemIcons.Application.Clone() : new Icon(stream);
    }
}

internal static class Drawing
{
    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Fill(Graphics g, Rectangle r, int radius, Color color)
    {
        using var path = Rounded(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void Outline(Graphics g, Rectangle r, int radius, Color color, float width)
    {
        using var path = Rounded(r, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }
}

internal static class Native
{
    public const int AnyProcess = -1;

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);

    /// <summary>Pauses or resumes drawing of a control and everything in it.</summary>
    public static void SetRedraw(Control control, bool on)
    {
        const int WmSetRedraw = 0x000B;
        if (control.IsHandleCreated)
            SendMessage(control.Handle, WmSetRedraw, on ? 1 : 0, IntPtr.Zero);
    }

    /// <summary>Draws a control and all its children right now, in one pass.</summary>
    public static void RedrawNow(Control control)
    {
        const uint Invalidate = 0x0001, Erase = 0x0004, AllChildren = 0x0080, UpdateNow = 0x0100, Frame = 0x0400;
        if (control.IsHandleCreated)
            RedrawWindow(control.Handle, IntPtr.Zero, IntPtr.Zero, Invalidate | Erase | AllChildren | UpdateNow | Frame);
    }

    /// <summary>Gives the window a dark title bar to match the page (Windows 10 2004 and later).</summary>
    public static void UseDarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkModeBefore20H1, ref on, sizeof(int));
    }

    /// <summary>Draws a control's scroll bars in Windows' dark style instead of light grey.</summary>
    public static void UseDarkScrollBars(Control control)
    {
        if (control.IsHandleCreated)
            SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        control.HandleCreated += (_, _) => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
    }

    /// <summary>Stops Windows from sleeping on its own while auto shutdown is watching.</summary>
    public static void KeepSystemAwake(bool on) =>
        SetThreadExecutionState(on ? EsContinuous | EsSystemRequired : EsContinuous);
}
