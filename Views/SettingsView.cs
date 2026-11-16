using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using Housekeeper.Core;
using Housekeeper.Ui;
using Housekeeper.Ui.Controls;

namespace Housekeeper.Views;

internal sealed class SettingsView : Page
{
    private readonly AppSettings _settings;
    private readonly ToggleBox _startup, _exitAfter;
    private readonly TextLine _startupNote;
    private bool _reverting;

    public SettingsView(AppSettings settings) : base("Settings")
    {
        _settings = settings;
        AutoScroll = true;

        SuspendLayout();
        var stack = new Stack { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        stack.SuspendLayout();
        stack.Add(Make.Title("Settings"));

        stack.Add(Make.Section("Startup"));
        _startup = stack.Add(new ToggleBox("Open when Windows starts") { Checked = settings.OpenAtStartup });
        stack.Add(Make.Note("Starts in the notification area and runs the automatic clean when it’s due.", indent: true));
        _startupNote = stack.Add(Make.Error(indent: true));
        _exitAfter = stack.Add(new ToggleBox("Close after the startup check") { Checked = settings.ExitAfterStartupCheck, Enabled = settings.OpenAtStartup });
        stack.Add(Make.Note("Exits once any due clean has finished, instead of staying in the notification area.", indent: true));

        stack.Add(Make.Section("Cleaning"));
        var keepRecent = stack.Add(new ToggleBox("Keep files changed in the last 24 hours") { Checked = settings.KeepRecentFiles });
        stack.Add(Make.Note("Installers and running apps may still need them.", indent: true));
        var showLog = stack.Add(new ToggleBox("Show the detailed log") { Checked = settings.ShowLog });

        stack.Add(Make.Section("Window"));
        var toTray = stack.Add(new ToggleBox("Minimize to the notification area") { Checked = settings.MinimizeToTray });
        stack.Add(Make.Note("Housekeeper keeps running there, so scheduled cleans and Auto Shutdown carry on.", indent: true));

        stack.Add(Make.Section("About"));
        stack.Add(Make.Note("Set in Montserrat, © 2011 The Montserrat Project Authors, under the SIL Open Font License 1.1."));
        var openLog = stack.Add(new FlatButton("Open Activity Log") { Anchor = AnchorStyles.Left });
        openLog.Margin = new Padding(0, 4, 0, 0);
        var logStatus = stack.Add(Make.Error());

        stack.ResumeLayout(false);
        Controls.Add(stack);
        ResumeLayout(false);

        _startup.CheckedChanged += (_, _) => OnStartupChanged();
        _exitAfter.CheckedChanged += (_, _) => Save(s => s.ExitAfterStartupCheck = _exitAfter.Checked);
        keepRecent.CheckedChanged += (_, _) => Save(s => s.KeepRecentFiles = keepRecent.Checked);
        showLog.CheckedChanged += (_, _) => Save(s => s.ShowLog = showLog.Checked);
        toTray.CheckedChanged += (_, _) => Save(s => s.MinimizeToTray = toTray.Checked);
        openLog.Click += (_, _) =>
        {
            try
            {
                if (!File.Exists(AppPaths.ActivityLogFile))
                    ActivityLog.Info("Activity log opened.");
                using var _ = Process.Start(new ProcessStartInfo(AppPaths.ActivityLogFile) { UseShellExecute = true });
                logStatus.Visible = false;
            }
            catch (Exception ex) when (ex is Win32Exception or IOException)
            {
                logStatus.Text = $"Couldn’t open {AppPaths.ActivityLogFile}: {ex.Message}";
                logStatus.Visible = true;
            }
        };
    }

    public override void OnPageVisible(bool visible)
    {
        if (visible)
            ShowStartupWarning();
    }

    private void Save(Action<AppSettings> change)
    {
        change(_settings);
        _settings.Save();
    }

    private void OnStartupChanged()
    {
        if (_reverting)
            return;
        bool on = _startup.Checked;
        try
        {
            StartupEntry.Apply(on);
            Save(s => s.OpenAtStartup = on);
            _exitAfter.Enabled = on;
            ShowStartupWarning();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            ActivityLog.Error("Couldn't change the startup entry.", ex);
            _startupNote.ForeColor = Theme.Bad;
            _startupNote.Text = $"Windows didn’t allow the change: {ex.Message}";
            _startupNote.Visible = true;
            _reverting = true;
            _startup.Checked = !on;
            _reverting = false;
        }
    }

    /// <summary>Says so when Windows starts a different copy of the app than this one.</summary>
    private void ShowStartupWarning()
    {
        string? registered = AppPaths.IsSandbox ? null : StartupEntry.RegisteredExe();
        bool other = _settings.OpenAtStartup && registered is not null && File.Exists(registered) && !StartupEntry.IsThisExe(registered);
        _startupNote.ForeColor = Theme.Caution;
        _startupNote.Text = other ? $"Windows starts the copy in {Path.GetDirectoryName(registered)}. Untick and tick again to use this one." : "";
        _startupNote.Visible = other;
    }
}
