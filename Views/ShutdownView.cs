using Housekeeper.Core;
using Housekeeper.Ui;
using Housekeeper.Ui.Controls;

namespace Housekeeper.Views;

/// <summary>Auto shutdown: turn the PC off (or put it to sleep) when downloads finish or after a set time.</summary>
internal sealed class ShutdownView : Page
{
    private readonly AppSettings _settings;
    private readonly ShutdownWatch _watch;
    private readonly TextLine _down, _up, _status, _error;
    private readonly ChoiceBox _onFinish, _onTimer, _shutDown, _sleep;
    private readonly ToggleBox _uploads;
    private readonly NumberBox _below, _quietMinutes, _timerMinutes;
    private readonly Control _when, _then, _finishOptions, _timerOptions;
    private readonly FlatButton _start;

    public ShutdownView(AppSettings settings, ShutdownWatch watch) : base("Auto Shutdown")
    {
        _settings = settings;
        _watch = watch;
        AutoScroll = true;

        SuspendLayout();
        var stack = new Stack { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        stack.SuspendLayout();
        stack.Add(Make.Title("Auto Shutdown"));

        // Room for the widest reading, so the numbers can change every second without anything moving.
        const string widest = "8,888 KB/s";
        _down = new TextLine("–") { Font = AppFonts.Strong, ForeColor = Theme.Ink, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 32, 0), ReserveText = widest };
        _up = new TextLine("–") { Font = AppFonts.Strong, ForeColor = Theme.Ink, Anchor = AnchorStyles.Left, ReserveText = widest };
        var live = stack.Add(Make.Row(Caption("Download"), _down, Caption("Upload"), _up));
        live.Margin = new Padding(0, 10, 0, 0);

        stack.Add(Make.Section("When"));
        var when = new Stack { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        _onFinish = when.Add(new ChoiceBox("When downloads finish") { Checked = settings.ShutdownTrigger == ShutdownTrigger.DownloadsFinish });
        _uploads = new ToggleBox("Wait for uploads too") { Checked = settings.WatchUploads };
        _below = new NumberBox(1, 100_000, settings.IdleBelowKBps, "Speed that counts as finished, in KB/s");
        _quietMinutes = new NumberBox(1, 240, settings.IdleMinutes, "Minutes the speed has to stay below that");
        var finish = new Stack { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(Theme.TickIndent, 0, 0, 6) };
        finish.Add(Make.Row(Make.Text("Finished means below"), _below, Make.Text("KB/s for"), _quietMinutes, Make.Text("minutes")));
        finish.Add(Make.Row(_uploads));
        _finishOptions = when.Add(finish);
        _onTimer = when.Add(new ChoiceBox("After a set time") { Checked = settings.ShutdownTrigger == ShutdownTrigger.Timer });
        _timerMinutes = new NumberBox(1, 1440, settings.TimerMinutes, "Minutes until the PC shuts down");
        _timerOptions = when.Add(Make.Row(true, Make.Text("In"), _timerMinutes, Make.Text("minutes")));
        _when = stack.Add(when);
        _error = stack.Add(Make.Error(indent: true));

        stack.Add(Make.Section("Then"));
        _shutDown = new ChoiceBox("Shut down") { Checked = settings.PowerAction == PowerAction.ShutDown };
        _sleep = new ChoiceBox("Sleep") { Checked = settings.PowerAction == PowerAction.Sleep };
        _then = stack.Add(Make.Row(_shutDown, _sleep));

        _start = new FlatButton("Start Watching", ButtonKind.Primary) { ReserveTexts = ["Stop Watching", "Cancel Shutdown", "Cancel Sleep"] };
        _status = new TextLine("");
        var actions = stack.Add(Make.Columns((_start, false), (_status, true)));
        actions.Margin = new Padding(0, 28, 0, 0);
        stack.Add(Make.Note("You get a one-minute warning first, with a chance to cancel.")).Margin = new Padding(0, 10, 0, 0);

        stack.ResumeLayout(false);
        Controls.Add(stack);
        ResumeLayout(false);

        _onFinish.CheckedChanged += (_, _) => Save(s => s.ShutdownTrigger = _onFinish.Checked ? ShutdownTrigger.DownloadsFinish : ShutdownTrigger.Timer);
        _uploads.CheckedChanged += (_, _) => Save(s => s.WatchUploads = _uploads.Checked);
        _shutDown.CheckedChanged += (_, _) => Save(s => s.PowerAction = _shutDown.Checked ? PowerAction.ShutDown : PowerAction.Sleep);
        _below.ValueChanged += (_, _) => Save(s => s.IdleBelowKBps = _below.Value);
        _quietMinutes.ValueChanged += (_, _) => Save(s => s.IdleMinutes = _quietMinutes.Value);
        _timerMinutes.ValueChanged += (_, _) => Save(s => s.TimerMinutes = _timerMinutes.Value);
        foreach (var box in new[] { _below, _quietMinutes, _timerMinutes })
            box.ErrorChanged += (_, _) => ShowError();
        _start.Click += (_, _) => StartOrStop();
        _watch.Changed += (_, _) => Render();

        Render();
    }

    public override void OnPageVisible(bool visible) => _watch.Sampling = visible;

    private static TextLine Caption(string text) => new(text)
    {
        ForeColor = Theme.Muted,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 0, 8, 0),
    };

    private void Save(Action<AppSettings> change)
    {
        change(_settings);
        _settings.Save();
        Render();
    }

    private void ShowError()
    {
        var error = new[] { _below, _quietMinutes, _timerMinutes }.Select(b => b.Error).FirstOrDefault(e => e is not null);
        _error.Text = error ?? "";
        _error.Visible = error is not null;
    }

    private void StartOrStop()
    {
        if (_watch.IsActive)
        {
            _watch.Stop(_watch.Phase == WatchPhase.Countdown ? "cancelled during the warning" : "stopped");
            return;
        }
        var boxes = _settings.ShutdownTrigger == ShutdownTrigger.Timer ? new[] { _timerMinutes } : new[] { _below, _quietMinutes };
        if (boxes.FirstOrDefault(b => b.Error is not null) is { } bad)
        {
            bad.FocusInput();
            return;
        }
        _watch.Start(WatchPlan.From(_settings));
    }

    private void Render()
    {
        bool sampling = _watch.Sampling || _watch.IsActive;
        _down.Text = sampling ? Format.Rate(_watch.Rate.Down) : "–";
        _up.Text = sampling ? Format.Rate(_watch.Rate.Up) : "–";

        bool active = _watch.IsActive;
        _when.Enabled = _then.Enabled = !active;
        _finishOptions.Enabled = !active && _onFinish.Checked;
        _timerOptions.Enabled = !active && _onTimer.Checked;

        var plan = _watch.Plan;
        bool sleep = (plan?.Action ?? _settings.PowerAction) == PowerAction.Sleep;
        _start.Text = _watch.Phase switch
        {
            WatchPhase.Countdown => sleep ? "Cancel Sleep" : "Cancel Shutdown",
            WatchPhase.Off => "Start Watching",
            _ => "Stop Watching",
        };
        _status.ForeColor = _watch.Phase == WatchPhase.Countdown ? Theme.Caution : Theme.Text;
        _status.Text = _watch.Phase switch
        {
            WatchPhase.Waiting when plan is not null => plan.WatchUploads ? "Waiting for a download or upload to start…" : "Waiting for a download to start…",
            WatchPhase.Busy when plan is not null => $"Transfer running. {(sleep ? "Sleeps" : "Shuts down")} once it stays below {plan.QuietBelow / 1024:N0} KB/s for {plan.QuietFor.TotalMinutes:N0} min.",
            WatchPhase.Quiet when plan is not null => $"Quiet for {Format.Clock(_watch.QuietTime)} of {Format.Clock(plan.QuietFor)}.",
            WatchPhase.Timer => $"{(sleep ? "Sleeping" : "Shutting down")} at {Format.Time(_watch.ActionTime)}, in {Format.Clock(_watch.ActionTime - DateTime.Now)}.",
            WatchPhase.Countdown => $"{(sleep ? "Going to sleep" : "Shutting down")} in {Math.Max(0, (int)Math.Ceiling((_watch.ActionTime - DateTime.Now).TotalSeconds))} s.",
            _ => _watch.LastMessage ?? "",
        };
    }
}
