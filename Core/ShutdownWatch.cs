using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Housekeeper.Core;

internal readonly record struct NetworkRate(double Down, double Up);

/// <summary>How fast the PC is receiving and sending, summed over every connected adapter.</summary>
internal sealed class NetworkMeter
{
    private readonly Stopwatch _clock = new();
    private readonly Dictionary<string, (long Received, long Sent)> _last = new();

    public NetworkRate Sample()
    {
        double seconds = _clock.Elapsed.TotalSeconds;
        bool first = !_clock.IsRunning;
        _clock.Restart();

        long down = 0, up = 0;
        var seen = new HashSet<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            IPInterfaceStatistics stats;
            try
            {
                stats = nic.GetIPStatistics();
            }
            catch (NetworkInformationException)
            {
                continue;
            }
            seen.Add(nic.Id);
            // Only adapters seen last time count, so one that just connected doesn't add a spike.
            if (_last.TryGetValue(nic.Id, out var before))
            {
                down += Math.Max(0, stats.BytesReceived - before.Received);
                up += Math.Max(0, stats.BytesSent - before.Sent);
            }
            _last[nic.Id] = (stats.BytesReceived, stats.BytesSent);
        }
        foreach (var gone in _last.Keys.Where(id => !seen.Contains(id)).ToList())
            _last.Remove(gone);

        return first || seconds < 0.2 ? default : new NetworkRate(down / seconds, up / seconds);
    }
}

internal enum WatchPhase { Off, Waiting, Busy, Quiet, Timer, Countdown }

internal sealed record WatchPlan(ShutdownTrigger Trigger, bool WatchUploads, double QuietBelow, TimeSpan QuietFor, TimeSpan TimerFor, PowerAction Action)
{
    public static WatchPlan From(AppSettings s) => new(
        s.ShutdownTrigger, s.WatchUploads, s.IdleBelowKBps * 1024.0,
        TimeSpan.FromMinutes(s.IdleMinutes), TimeSpan.FromMinutes(s.TimerMinutes), s.PowerAction);
}

/// <summary>
/// Auto shutdown. Samples the network once a second; once a download has run and then stayed
/// below the threshold long enough (or the timer is up), gives a one-minute warning and then
/// shuts down or sleeps. While watching it keeps Windows from sleeping on its own, which would
/// otherwise stop the download it is waiting for.
/// </summary>
internal sealed class ShutdownWatch : IDisposable
{
    public static readonly TimeSpan Warning = TimeSpan.FromSeconds(60);

    private readonly NetworkMeter _meter = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private bool _sampling;
    private DateTime _quietSince, _deadline;

    public ShutdownWatch() => _timer.Tick += (_, _) => Tick();

    public WatchPhase Phase { get; private set; }
    public WatchPlan? Plan { get; private set; }
    public NetworkRate Rate { get; private set; }
    public bool IsActive => Phase != WatchPhase.Off;

    /// <summary>What happened last time the watch ended on its own, for the status line.</summary>
    public string? LastMessage { get; private set; }

    /// <summary>How long the network has been quiet, while <see cref="Phase"/> is Quiet.</summary>
    public TimeSpan QuietTime => Phase == WatchPhase.Quiet ? DateTime.Now - _quietSince : TimeSpan.Zero;

    /// <summary>When the PC shuts down or sleeps, once the timer or the warning is running.</summary>
    public DateTime ActionTime { get; private set; }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    /// <summary>Raised once a second while sampling, and whenever the phase changes.</summary>
    public event EventHandler? Changed;

    public event EventHandler? CountdownStarted;

    /// <summary>Keep measuring even when not watching, so the screen can show live speeds.</summary>
    public bool Sampling
    {
        get => _sampling;
        set
        {
            _sampling = value;
            UpdateTimer();
        }
    }

    public void Start(WatchPlan plan)
    {
        Plan = plan;
        LastMessage = null;
        if (plan.Trigger == ShutdownTrigger.Timer)
        {
            // The warning is part of the time, so the PC goes off when the user asked, not a minute later.
            var now = DateTime.Now;
            Phase = WatchPhase.Timer;
            _deadline = now + (plan.TimerFor > Warning ? plan.TimerFor - Warning : TimeSpan.Zero);
            ActionTime = now + Max(plan.TimerFor, Warning);
        }
        else
        {
            Phase = WatchPhase.Waiting;
        }
        Ui.Native.KeepSystemAwake(true);
        ActivityLog.Info(plan.Trigger == ShutdownTrigger.Timer
            ? $"Auto shutdown: {Describe(plan.Action)} at {ActionTime:t}."
            : $"Auto shutdown: {Describe(plan.Action)} once downloads{(plan.WatchUploads ? " and uploads" : "")} stay below {plan.QuietBelow / 1024:N0} KB/s for {plan.QuietFor.TotalMinutes:N0} min.");
        UpdateTimer();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Stop(string reason)
    {
        if (Phase == WatchPhase.Off)
            return;
        End();
        LastMessage = null;
        ActivityLog.Info($"Auto shutdown {reason}.");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static string Describe(PowerAction action) => action == PowerAction.Sleep ? "sleep" : "shut down";

    private void Tick()
    {
        Rate = _meter.Sample();
        if (Plan is { } plan)
            Advance(plan, DateTime.Now);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Advance(WatchPlan plan, DateTime now)
    {
        bool busy = Rate.Down >= plan.QuietBelow || (plan.WatchUploads && Rate.Up >= plan.QuietBelow);
        switch (Phase)
        {
            // Wait for a transfer to start first; otherwise starting the watch before the download
            // begins would shut the PC down a few minutes later.
            case WatchPhase.Waiting when busy:
            case WatchPhase.Quiet when busy:
                Phase = WatchPhase.Busy;
                break;
            case WatchPhase.Busy when !busy:
                Phase = WatchPhase.Quiet;
                _quietSince = now;
                break;
            case WatchPhase.Quiet when now - _quietSince >= plan.QuietFor:
            case WatchPhase.Timer when now >= _deadline:
                Phase = WatchPhase.Countdown;
                _deadline = now + Warning;
                ActionTime = _deadline;
                ActivityLog.Info($"Auto shutdown: going to {Describe(plan.Action)} in {Warning.TotalSeconds:N0} seconds.");
                CountdownStarted?.Invoke(this, EventArgs.Empty);
                break;
            case WatchPhase.Countdown when now >= _deadline:
                End();
                Act(plan.Action);
                break;
        }
    }

    private void Act(PowerAction action)
    {
        if (AppPaths.IsSandbox)
        {
            LastMessage = $"Sandbox: the PC would {Describe(action)} now.";
            ActivityLog.Info($"Sandbox: skipped the {Describe(action)}.");
            return;
        }
        try
        {
            ActivityLog.Info($"Auto shutdown: {Describe(action)} now.");
            if (action == PowerAction.Sleep)
            {
                if (!Application.SetSuspendState(PowerState.Suspend, force: false, disableWakeEvent: false))
                    throw new Win32Exception();
            }
            else
            {
                using var _ = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), "/s /t 0")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            ActivityLog.Error($"Auto shutdown couldn't {Describe(action)}.", ex);
            LastMessage = $"Windows didn’t {Describe(action)}: {ex.Message}";
        }
    }

    private void End()
    {
        Phase = WatchPhase.Off;
        Plan = null;
        Ui.Native.KeepSystemAwake(false);
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        bool run = _sampling || IsActive;
        if (run && !_timer.Enabled)
        {
            _meter.Sample();
            _timer.Start();
        }
        else if (!run && _timer.Enabled)
        {
            _timer.Stop();
            Rate = default;
        }
    }

    public void Dispose()
    {
        if (IsActive)
            Stop("stopped because Housekeeper closed");
        _timer.Dispose();
    }
}
