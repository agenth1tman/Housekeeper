using Housekeeper.Ui;

namespace Housekeeper.Views;

/// <summary>A screen of the app, shown to the right of the sidebar.</summary>
internal abstract class Page : UserControl
{
    protected Page(string title)
    {
        Title = title;
        AutoScaleMode = AutoScaleMode.Inherit;
        BackColor = Theme.Page;
        ForeColor = Theme.Text;
        Font = AppFonts.Body;
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        DoubleBuffered = true;
        AccessibleName = title;
        Native.UseDarkScrollBars(this);
    }

    public string Title { get; }

    /// <summary>The page came on screen, or left it (another page, or the window went to the tray).</summary>
    public virtual void OnPageVisible(bool visible)
    {
    }

    /// <summary>A key the page handles itself, such as F5.</summary>
    public virtual bool HandleShortcut(Keys keys) => false;
}
