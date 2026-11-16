using System.ComponentModel;
using System.Diagnostics;
using Housekeeper.Core;
using Housekeeper.Ui;
using Housekeeper.Ui.Controls;
using Microsoft.Win32;

namespace Housekeeper.Views;

/// <summary>
/// The window: sidebar, pages, tray icon. It also owns the automatic-clean check, which runs at
/// launch, every ten minutes while the app is open (so a PC that stays on for days still gets
/// cleaned on time) and after the PC wakes up.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly TimeSpan ScheduleCheckInterval = TimeSpan.FromMinutes(10);

    private readonly AppSettings _settings;
    private readonly CleanController _cleaner;
    private readonly ShutdownWatch _watch;
    private readonly CleanerView _cleanerPage;
    private readonly ShutdownView _shutdownPage;
    private readonly NotifyIcon _tray;
    private readonly ToolTip _tips = new();
    private readonly System.Windows.Forms.Timer _scheduleTimer;
    private readonly Panel _host;
    private readonly Control _sidebar;
    private readonly List<(FlatButton Button, Page Page)> _pages = new();
    private Page? _current;
    private FormWindowState _restoreState = FormWindowState.Normal;
    private bool _wasMinimized, _shownOnce, _launchChecked, _exiting;

    public MainForm(AppSettings settings)
    {
        _settings = settings;
        _cleaner = new CleanController(settings);
        _watch = new ShutdownWatch();

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = AppPaths.IsSandbox ? "Housekeeper (sandbox)" : "Housekeeper";
        Icon = AppIcon.Window;
        BackColor = Theme.Page;
        ForeColor = Theme.Text;
        Font = AppFonts.Body;
        ClientSize = new Size(1040, 700);
        MinimumSize = new Size(900, 640);
        StartPosition = FormStartPosition.CenterScreen;

        _cleanerPage = new CleanerView(settings, _cleaner);
        _shutdownPage = new ShutdownView(settings, _watch);
        _host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.Gutter, 30, Theme.Gutter, 24) };
        AddPage(_cleanerPage, "Ctrl+1");
        AddPage(_shutdownPage, "Ctrl+2");
        AddPage(new SettingsView(settings), "Ctrl+3");

        // The fill area goes in first: docking works from the last control added.
        Controls.Add(_host);
        Controls.Add(_sidebar = BuildSidebar());
        ShowPage(_pages.Find(p => string.Equals(p.Page.Title, settings.LastPage, StringComparison.OrdinalIgnoreCase)).Page ?? _cleanerPage);
        ResumeLayout();

        _tray = BuildTray();
        _watch.CountdownStarted += (_, _) => OnCountdown();
        _watch.Changed += (_, _) => UpdateTrayText();

        _scheduleTimer = new System.Windows.Forms.Timer { Interval = (int)ScheduleCheckInterval.TotalMilliseconds };
        _scheduleTimer.Tick += async (_, _) => await CheckScheduleAsync("regular check");
        _scheduleTimer.Start();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>Starts hidden in the notification area, as when Windows starts the app.</summary>
    public void StartInTray()
    {
        _ = Handle; // a window handle without showing the window: timers, BeginInvoke and the tray need one
        _launchChecked = true;
        BeginInvoke(new Action(async () => await StartupCheckAsync()));
    }

    public void ShowFromTray()
    {
        if (!Visible)
            Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = _restoreState;
        Activate();
    }

    /// <summary>Called on a thread-pool thread when the exe is launched again.</summary>
    public void ShowFromAnotherInstance()
    {
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(new Action(ShowFromTray));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.UseDarkTitleBar(Handle);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Restored here rather than in the constructor so DPI scaling doesn't apply twice.
        if (_settings.WindowBounds is [var x, var y, var width, var height] && width > 0 && height > 0)
        {
            var bounds = new Rectangle(x, y, width, height);
            if (Screen.AllScreens.Any(s => s.WorkingArea.Contains(new Point(bounds.X + bounds.Width / 2, bounds.Y + 8))))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = bounds;
            }
        }
        if (_settings.WindowMaximized)
            WindowState = FormWindowState.Maximized;

        // Build every page's windows and layout now, before the form is on screen. Switching pages
        // then only swaps which one is visible, instead of building it while you watch.
        foreach (var (_, page) in _pages)
        {
            if (page == _current)
                continue;
            page.Visible = true;
            page.CreateControl();
            page.PerformLayout();
            page.Visible = false;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _shownOnce = true;
        _current?.OnPageVisible(true);
        if (!_launchChecked)
        {
            _launchChecked = true;
            _ = CheckScheduleAsync("launch");
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_shownOnce)
            _current?.OnPageVisible(Visible && WindowState != FormWindowState.Minimized);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        bool minimized = WindowState == FormWindowState.Minimized;
        if (!minimized)
            _restoreState = WindowState;
        if (minimized == _wasMinimized)
            return;
        _wasMinimized = minimized;
        _current?.OnPageVisible(!minimized && Visible);
        if (minimized && _settings.MinimizeToTray)
            Hide();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        int page = keyData switch
        {
            Keys.Control | Keys.D1 or Keys.Control | Keys.NumPad1 => 0,
            Keys.Control | Keys.D2 or Keys.Control | Keys.NumPad2 => 1,
            Keys.Control | Keys.D3 or Keys.Control | Keys.NumPad3 => 2,
            _ => -1,
        };
        if (page >= 0)
        {
            ShowPage(_pages[page].Page);
            return true;
        }
        return _current?.HandleShortcut(keyData) == true || base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing && _watch.IsActive && !ConfirmStopWatch())
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
        if (e.Cancel)
            return;
        _cleaner.Cancel();
        _watch.Stop("stopped because Housekeeper closed");
        if (_shownOnce)
            SaveWindowState();
        _tray.Visible = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _scheduleTimer.Dispose();
            _tray.Dispose();
            _tips.Dispose();
            _watch.Dispose();
            _cleaner.Dispose();
        }
        base.Dispose(disposing);
    }

    private void AddPage(Page page, string shortcut)
    {
        var button = new FlatButton(page.Title, ButtonKind.Nav);
        _tips.SetToolTip(button, shortcut);
        button.Click += (_, _) => ShowPage(page);
        page.Visible = false;
        _host.Controls.Add(page);
        _pages.Add((button, page));
    }

    private void ShowPage(Page page)
    {
        if (_current == page)
            return;

        // Drawing is paused for the swap and done in one go afterwards, so the new page never
        // appears half-drawn.
        bool onScreen = IsHandleCreated && Visible;
        if (onScreen)
        {
            Native.SetRedraw(_host, false);
            Native.SetRedraw(_sidebar, false);
        }
        try
        {
            _host.SuspendLayout();
            if (_current is not null)
            {
                _current.OnPageVisible(false);
                _current.Visible = false;
            }
            page.Visible = true;
            _current = page;
            foreach (var (button, p) in _pages)
                button.Selected = p == page;
            _host.ResumeLayout(true);
        }
        finally
        {
            if (onScreen)
            {
                Native.SetRedraw(_host, true);
                Native.SetRedraw(_sidebar, true);
                Native.RedrawNow(_sidebar);
                Native.RedrawNow(_host);
            }
        }
        if (_shownOnce && Visible)
            page.OnPageVisible(true);
    }

    private Control BuildSidebar()
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = Theme.SidebarWidth,
            BackColor = Theme.Sidebar,
            Padding = new Padding(14, 22, 14, 16),
        };
        sidebar.SuspendLayout();
        var stack = new Stack { Dock = DockStyle.Fill };
        stack.SuspendLayout();

        var mark = new PictureBox
        {
            Image = AppIcon.Image(32),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(26, 26),
            Margin = new Padding(4, 0, 10, 0),
        };
        var brand = Make.Row(mark, new TextLine("Housekeeper") { Font = AppFonts.Brand, ForeColor = Theme.Ink, Margin = Padding.Empty });
        brand.Margin = new Padding(0, 0, 0, 26);
        stack.Add(brand);
        foreach (var (button, _) in _pages)
            stack.Add(button);
        stack.AddSpace();
        stack.Add(new TextLine($"Version {AppInfo.Version}") { Font = AppFonts.Small, ForeColor = Theme.Muted, Margin = new Padding(4, 0, 0, 2) });
        stack.Add(BuildCredit());

        stack.ResumeLayout(false);
        sidebar.Controls.Add(stack);
        sidebar.ResumeLayout(false);
        return sidebar;
    }

    private LinkLabel BuildCredit()
    {
        var text = $"Made by {AppInfo.Author}";
        var credit = new LinkLabel
        {
            Text = text,
            LinkArea = new LinkArea(text.Length - AppInfo.Author.Length, AppInfo.Author.Length),
            AutoSize = true,
            UseMnemonic = false,
            Font = AppFonts.Small,
            ForeColor = Theme.Muted,
            LinkColor = Theme.Text,
            ActiveLinkColor = Theme.Ink,
            VisitedLinkColor = Theme.Text,
            LinkBehavior = LinkBehavior.HoverUnderline,
            // LinkLabel pads its text a little; this lines it up with the version above.
            Margin = new Padding(2, 0, 0, 0),
            Anchor = AnchorStyles.Left,
        };
        _tips.SetToolTip(credit, AppInfo.AuthorUrl);
        credit.LinkClicked += (_, _) =>
        {
            try
            {
                using var _ = Process.Start(new ProcessStartInfo(AppInfo.AuthorUrl) { UseShellExecute = true });
            }
            catch (Win32Exception ex)
            {
                ActivityLog.Error($"Couldn't open {AppInfo.AuthorUrl}.", ex);
            }
        };
        return credit;
    }

    private NotifyIcon BuildTray()
    {
        var open = new ToolStripMenuItem("Open Housekeeper", null, (_, _) => ShowFromTray()) { Font = AppFonts.Strong };
        var clean = new ToolStripMenuItem("Clean Temp Folder Now", null, async (_, _) => await CleanFromTrayAsync());
        var stopWatch = new ToolStripMenuItem("Stop Auto Shutdown", null, (_, _) => _watch.Stop("stopped from the notification area"));
        var exit = new ToolStripMenuItem("Exit", null, (_, _) => ExitApp());
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = AppFonts.Body, ShowImageMargin = false };
        menu.Items.AddRange(new ToolStripItem[] { open, clean, stopWatch, new ToolStripSeparator(), exit });
        menu.Opening += (_, _) =>
        {
            clean.Enabled = !_cleaner.IsRunning;
            stopWatch.Visible = _watch.IsActive;
        };

        var tray = new NotifyIcon { Icon = AppIcon.Tray(), Text = "Housekeeper", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowFromTray();
        };
        tray.BalloonTipClicked += (_, _) => ShowFromTray();
        return tray;
    }

    private async Task CleanFromTrayAsync()
    {
        var summary = await _cleaner.RunAsync(automatic: false);
        if (summary is not null && !IsOnScreen)
            _tray.ShowBalloonTip(6000, "Temp Folder Cleaned", CleanerView.Describe(summary), ToolTipIcon.None);
    }

    private bool IsOnScreen => Visible && WindowState != FormWindowState.Minimized;

    /// <summary>Runs the automatic clean if it's due. Returns true when it showed a notification.</summary>
    private async Task<bool> CheckScheduleAsync(string reason)
    {
        if (_cleaner.IsRunning || !CleanSchedule.IsDue(_settings, DateTime.Now))
            return false;
        ActivityLog.Info($"Automatic clean is due ({reason}).");
        var summary = await _cleaner.RunAsync(automatic: true);
        if (summary is null || IsOnScreen)
            return false;
        _tray.ShowBalloonTip(6000, "Temp Folder Cleaned", CleanerView.Describe(summary), ToolTipIcon.None);
        return true;
    }

    private async Task StartupCheckAsync()
    {
        bool notified = await CheckScheduleAsync("Windows startup");
        if (!_settings.ExitAfterStartupCheck || Visible || _watch.IsActive)
            return;
        if (notified)
            await Task.Delay(TimeSpan.FromSeconds(8)); // time to read the notification before the icon goes
        if (!Visible && !_watch.IsActive)
        {
            ActivityLog.Info("Closing after the startup check.");
            ExitApp();
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && IsHandleCreated)
            BeginInvoke(new Action(async () => await CheckScheduleAsync("PC woke up")));
    }

    private void OnCountdown()
    {
        ShowPage(_shutdownPage);
        ShowFromTray();
        var what = _watch.Plan?.Action == PowerAction.Sleep ? "Going to Sleep" : "Shutting Down";
        _tray.ShowBalloonTip(10000, $"{what} in 1 Minute", "Open Housekeeper to cancel.", ToolTipIcon.Warning);
    }

    private void UpdateTrayText()
    {
        var text = _watch.IsActive ? "Housekeeper: Auto Shutdown is on" : "Housekeeper";
        if (_tray.Text != text)
            _tray.Text = text;
    }

    private bool ConfirmStopWatch()
    {
        var keep = new TaskDialogButton("Keep Watching");
        var stop = new TaskDialogButton("Stop and Close");
        var page = new TaskDialogPage
        {
            Caption = "Housekeeper",
            Heading = "Auto Shutdown is still on",
            Text = "Closing Housekeeper stops it, and the PC will stay on.",
            Icon = TaskDialogIcon.Warning,
            Buttons = { keep, stop },
            DefaultButton = keep,
        };
        return TaskDialog.ShowDialog(this, page) == stop;
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private void SaveWindowState()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowBounds = [bounds.X, bounds.Y, bounds.Width, bounds.Height];
        _settings.WindowMaximized = WindowState == FormWindowState.Maximized
            || (WindowState == FormWindowState.Minimized && _restoreState == FormWindowState.Maximized);
        _settings.LastPage = _current?.Title;
        _settings.Save();
    }
}
