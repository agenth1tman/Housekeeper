using System.Collections.Concurrent;

namespace Housekeeper.Core;

/// <summary>
/// Runs one clean at a time for both the window and the tray, keeps the log of the last clean,
/// and records the result. Rows found by the worker are handed to the UI in batches, a few times a
/// second, instead of one UI update per file.
/// </summary>
internal sealed class CleanController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly ConcurrentQueue<CleanItem> _incoming = new();
    private readonly List<CleanItem> _items = new();
    private readonly System.Windows.Forms.Timer _flush = new() { Interval = 150 };
    private CancellationTokenSource? _cancel;

    public CleanController(AppSettings settings)
    {
        _settings = settings;
        _flush.Tick += (_, _) => Flush();
    }

    /// <summary>The log of the current or last clean. Only touch it on the UI thread.</summary>
    public IReadOnlyList<CleanItem> Items => _items;

    public CleanProgress Progress { get; private set; } = new();

    public bool IsRunning => _cancel is not null;

    public event EventHandler? Started;

    /// <summary>New log rows or progress, at most a few times a second.</summary>
    public event EventHandler? Changed;

    public event EventHandler<CleanSummary>? Finished;

    /// <summary>Cleans the temp folder. Returns null when a clean was already running.</summary>
    public async Task<CleanSummary?> RunAsync(bool automatic)
    {
        if (_cancel is not null)
            return null;

        var cancel = _cancel = new CancellationTokenSource();
        var progress = Progress = new CleanProgress();
        _items.Clear();
        _incoming.Clear();
        Started?.Invoke(this, EventArgs.Empty);
        _flush.Start();

        var folder = AppPaths.TempDirectory;
        bool keepRecent = _settings.KeepRecentFiles;
        CleanSummary summary;
        try
        {
            summary = await Task.Run(() => TempCleaner.Clean(folder, keepRecent, _incoming.Enqueue, progress, cancel.Token));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ActivityLog.Error("Cleaning failed.", ex);
            summary = new CleanSummary(0, 0, 0, Stopped: false, Error: ex.Message);
        }
        finally
        {
            _flush.Stop();
            Flush();
            _cancel = null;
            cancel.Dispose();
        }

        Record(summary, automatic);
        Finished?.Invoke(this, summary);
        return summary;
    }

    public void Cancel() => _cancel?.Cancel();

    private void Flush()
    {
        while (_incoming.TryDequeue(out var item))
            _items.Add(item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Record(CleanSummary summary, bool automatic)
    {
        var now = DateTime.Now;
        if (summary.Error is null)
        {
            _settings.LastCleaned = now;
            _settings.LastCleanedBytes = summary.BytesFreed;
            _settings.LastCleanedFiles = summary.FilesDeleted;
        }
        // Restart the countdown even after a failure, so a broken clean is retried after the
        // interval rather than every few minutes.
        if (_settings.AutoClean)
            _settings.AutoCleanFrom = now;
        _settings.Save();

        var kind = automatic ? "Automatic clean" : "Clean";
        ActivityLog.Info(summary.Error is { } error
            ? $"{kind} failed: {error}"
            : $"{kind} {(summary.Stopped ? "stopped" : "finished")}: {summary.FilesDeleted} files deleted, {summary.BytesFreed} bytes freed, {summary.ItemsLeft} items left. Next automatic clean: {CleanSchedule.NextClean(_settings)?.ToString("d") ?? "off"}.");
    }

    public void Dispose()
    {
        _cancel?.Cancel();
        _flush.Dispose();
    }
}
