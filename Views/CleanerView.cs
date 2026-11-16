using System.Globalization;
using Housekeeper.Core;
using Housekeeper.Ui;
using Housekeeper.Ui.Controls;

namespace Housekeeper.Views;

/// <summary>
/// The temp cleaner: how much is in the temp folder, the button that empties it, the automatic
/// schedule, and the log of the last clean.
/// </summary>
internal sealed class CleanerView : Page
{
    private static readonly TimeSpan MeasureAgainAfter = TimeSpan.FromSeconds(30);

    private readonly AppSettings _settings;
    private readonly CleanController _cleaner;
    private readonly TextLine _size, _sizeDetail, _result, _next, _daysError, _logStatus;
    private readonly FlatButton _clean, _saveLog;
    private readonly ToggleBox _auto;
    private readonly NumberBox _days;
    private readonly LogGrid _log;
    private readonly Control _logHeader;
    private CancellationTokenSource? _measuring;
    private DateTime _measuredAt = DateTime.MinValue;
    private bool _measuredKeepRecent, _visible;

    public CleanerView(AppSettings settings, CleanController cleaner) : base("Temp Cleaner")
    {
        _settings = settings;
        _cleaner = cleaner;

        SuspendLayout();
        var stack = new Stack { Dock = DockStyle.Fill };
        stack.SuspendLayout();
        stack.Add(Make.Title("Temp Cleaner"));
        stack.Add(Make.Note(AppPaths.TempDirectory));

        _size = stack.Add(new TextLine("–") { Font = AppFonts.Figure, ForeColor = Theme.Ink, Margin = new Padding(0, 18, 0, 0) });
        _sizeDetail = stack.Add(new TextLine("Measuring…") { ForeColor = Theme.Muted });

        _clean = new FlatButton("Clean Now", ButtonKind.Primary) { ReserveTexts = ["Stop"] };
        _result = new TextLine("");
        var actions = stack.Add(Make.Columns((_clean, false), (_result, true)));
        actions.Margin = new Padding(0, 20, 0, 0);

        _auto = new ToggleBox("Clean automatically every") { Checked = settings.AutoClean };
        _days = new NumberBox(1, 365, settings.AutoCleanDays, "Days between automatic cleans");
        var schedule = stack.Add(Make.Row(_auto, _days, Make.Text("days")));
        schedule.Margin = new Padding(0, 22, 0, 0);
        _daysError = stack.Add(Make.Error(indent: true));
        _next = stack.Add(Make.Note("", indent: true));

        _logStatus = new TextLine("") { Font = AppFonts.Small, ForeColor = Theme.Muted, AlignRight = true, Margin = new Padding(0, 0, 12, 0) };
        _saveLog = new FlatButton("Save Log…") { Margin = Padding.Empty, Enabled = false };
        var logTitle = Make.Section("Log");
        logTitle.Margin = Padding.Empty;
        _logHeader = stack.Add(Make.Columns((logTitle, false), (_logStatus, true), (_saveLog, false)));
        _logHeader.Margin = new Padding(0, 22, 0, 6);
        _log = stack.Add(new LogGrid { EmptyText = "Nothing cleaned yet in this session. Choose Clean Now to empty the temp folder." }, fill: true);
        _log.Bind(cleaner.Items);

        stack.ResumeLayout(false);
        Controls.Add(stack);
        ResumeLayout(false);

        _clean.Click += async (_, _) =>
        {
            if (_cleaner.IsRunning)
                _cleaner.Cancel();
            else
                await _cleaner.RunAsync(automatic: false);
        };
        _auto.CheckedChanged += (_, _) =>
        {
            _settings.AutoClean = _auto.Checked;
            // The countdown starts now, so switching it on never deletes anything straight away.
            if (_auto.Checked)
                _settings.AutoCleanFrom = DateTime.Now;
            _settings.Save();
        };
        _days.ValueChanged += (_, _) =>
        {
            _settings.AutoCleanDays = _days.Value;
            _settings.Save();
        };
        _days.ErrorChanged += (_, _) =>
        {
            _daysError.Text = _days.Error ?? "";
            _daysError.Visible = _days.Error is not null;
        };
        _saveLog.Click += (_, _) => SaveLog();

        _cleaner.Started += (_, _) => OnCleanStarted();
        _cleaner.Changed += (_, _) => OnCleanProgress();
        _cleaner.Finished += (_, summary) => OnCleanFinished(summary);
        _settings.Saved += (_, _) => OnSettingsSaved();

        ShowLastClean();
        UpdateNext();
        ApplyShowLog();
        // Start measuring straight away, so the size is usually there by the time the window is.
        _ = MeasureAsync();
    }

    /// <summary>One sentence about a finished clean, for the page and for notifications.</summary>
    public static string Describe(CleanSummary summary)
    {
        if (summary.Error is { } error)
            return $"Couldn’t clean the temp folder: {error}";
        var text = $"Freed {Format.Bytes(summary.BytesFreed)} from {Format.Count(summary.FilesDeleted, "file", "files")}.";
        if (summary.ItemsLeft > 0)
            text += $" {Format.Count(summary.ItemsLeft, "item was", "items were")} in use or recent and stayed.";
        return summary.Stopped ? "Stopped. " + text : text;
    }

    public override void OnPageVisible(bool visible)
    {
        _visible = visible;
        if (visible && _measuring is null && !_cleaner.IsRunning && (DateTime.Now - _measuredAt > MeasureAgainAfter || _measuredKeepRecent != _settings.KeepRecentFiles))
            _ = MeasureAsync();
    }

    public override bool HandleShortcut(Keys keys)
    {
        if (keys != Keys.F5 || _cleaner.IsRunning)
            return false;
        _ = MeasureAsync();
        return true;
    }

    private void OnCleanStarted()
    {
        _measuring?.Cancel();
        // The size is measured again when the clean ends; until then don't claim to be measuring.
        if (_measuredAt == DateTime.MinValue)
            _sizeDetail.Text = "";
        _clean.Text = "Stop";
        _result.ForeColor = Theme.Text;
        _result.Text = "Cleaning…";
        _saveLog.Enabled = false;
        _logStatus.Text = "";
        _log.Bind(_cleaner.Items);
    }

    private void OnCleanProgress()
    {
        _log.Sync();
        if (!_cleaner.IsRunning)
            return;
        var progress = _cleaner.Progress;
        _result.Text = $"Cleaning… {Format.Count(progress.Files, "file", "files")} deleted, {Format.Bytes(progress.Bytes)} so far";
    }

    private void OnCleanFinished(CleanSummary summary)
    {
        _clean.Text = "Clean Now";
        _result.ForeColor = summary.Error is null ? Theme.Text : Theme.Bad;
        _result.Text = Describe(summary);
        _log.Sync();
        _saveLog.Enabled = _cleaner.Items.Count > 0;
        _ = MeasureAsync();
    }

    private void OnSettingsSaved()
    {
        UpdateNext();
        ApplyShowLog();
        if (_visible && _measuredKeepRecent != _settings.KeepRecentFiles && !_cleaner.IsRunning)
            _ = MeasureAsync();
    }

    private async Task MeasureAsync()
    {
        _measuring?.Cancel();
        var cancel = _measuring = new CancellationTokenSource();
        bool keepRecent = _settings.KeepRecentFiles;
        if (_measuredAt == DateTime.MinValue)
            _sizeDetail.Text = "Measuring…";
        try
        {
            var size = await Task.Run(() => TempCleaner.Measure(AppPaths.TempDirectory, keepRecent, cancel.Token), cancel.Token);
            _measuredAt = DateTime.Now;
            _measuredKeepRecent = keepRecent;
            ShowSize(size, keepRecent);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _size.Text = "–";
            _sizeDetail.Text = $"Couldn’t read the temp folder: {ex.Message}";
        }
        finally
        {
            if (_measuring == cancel)
                _measuring = null;
            cancel.Dispose();
        }
    }

    private void ShowSize(FolderSize size, bool keepRecent)
    {
        _size.Text = Format.Bytes(size.Bytes);
        if (size.Files == 0)
            _sizeDetail.Text = "The temp folder is empty.";
        else if (keepRecent && size.RemovableBytes == 0)
            _sizeDetail.Text = $"in {Format.Count(size.Files, "file", "files")}, all changed in the last 24 hours, so they stay for now.";
        else if (keepRecent && size.RemovableBytes < size.Bytes)
            _sizeDetail.Text = $"in {Format.Count(size.Files, "file", "files")}. {Format.Bytes(size.RemovableBytes)} can go now; the rest changed in the last 24 hours.";
        else
            _sizeDetail.Text = $"in {Format.Count(size.Files, "file", "files")}.";
    }

    private void ShowLastClean()
    {
        _result.ForeColor = Theme.Muted;
        _result.Text = _settings.LastCleaned is { } last
            ? $"Last cleaned {Format.Day(last)}: freed {Format.Bytes(_settings.LastCleanedBytes)}."
            : "";
    }

    private void UpdateNext()
    {
        _next.Visible = _settings.AutoClean;
        if (CleanSchedule.NextClean(_settings) is not { } next)
            return;
        int days = (next.Date - DateTime.Today).Days;
        _next.Text = days switch
        {
            <= 0 => "Next clean: today.",
            1 => "Next clean: tomorrow.",
            _ => $"Next clean: {next.ToString("dddd d MMMM", CultureInfo.CurrentCulture)}, in {days} days.",
        };
    }

    private void ApplyShowLog()
    {
        _logHeader.Visible = _settings.ShowLog;
        _log.Visible = _settings.ShowLog;
    }

    private void SaveLog()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save Cleaning Log",
            FileName = $"Housekeeper log {DateTime.Now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture)}.txt",
            Filter = "Text file (*.txt)|*.txt",
            DefaultExt = "txt",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            File.WriteAllLines(dialog.FileName, _cleaner.Items.Select(i => $"{LogGrid.Describe(i.Outcome)}\t{i.DisplayName}\t{Format.Bytes(i.BytesFreed)}"));
            _logStatus.ForeColor = Theme.Muted;
            _logStatus.Text = $"Saved as {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logStatus.ForeColor = Theme.Bad;
            _logStatus.Text = $"Couldn’t save: {ex.Message}";
        }
    }
}
