namespace Housekeeper.Core;

internal enum CleanOutcome { Deleted, Partly, InUse, NoAccess, Recent }

/// <summary>One top-level file or folder of the temp folder, after cleaning.</summary>
internal sealed record CleanItem(string Name, bool IsFolder, CleanOutcome Outcome, long BytesFreed, int FilesDeleted, int ItemsLeft)
{
    public string DisplayName => IsFolder ? Name + Path.DirectorySeparatorChar : Name;
}

internal sealed record CleanSummary(int FilesDeleted, int ItemsLeft, long BytesFreed, bool Stopped, string? Error = null);

internal readonly record struct FolderSize(long Bytes, int Files, long RemovableBytes);

/// <summary>Running totals of a clean in progress, read by the UI while the cleaner writes them.</summary>
internal sealed class CleanProgress
{
    private int _files;
    private long _bytes;

    public int Files => Volatile.Read(ref _files);
    public long Bytes => Interlocked.Read(ref _bytes);

    public void Add(long bytes)
    {
        Interlocked.Increment(ref _files);
        Interlocked.Add(ref _bytes, bytes);
    }
}

/// <summary>Measures and empties the temp folder. Both run on a worker thread.</summary>
internal static class TempCleaner
{
    /// <summary>Files changed this recently may belong to an installer or app that is still running.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromHours(24);

    private static readonly EnumerationOptions OneLevel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

    // Skipping reparse points means junctions and symbolic links are never followed out of the folder.
    private static readonly EnumerationOptions AllLevels = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public static FolderSize Measure(string folder, bool keepRecent, CancellationToken token)
    {
        var root = new DirectoryInfo(folder);
        if (!root.Exists)
            return default;

        var cutoff = DateTime.UtcNow - RecentWindow;
        long bytes = 0, removable = 0;
        int files = 0;
        foreach (var file in root.EnumerateFiles("*", AllLevels))
        {
            if ((++files & 1023) == 0)
                token.ThrowIfCancellationRequested();
            bytes += file.Length;
            if (!keepRecent || !IsRecent(file, cutoff))
                removable += file.Length;
        }
        return new FolderSize(bytes, files, removable);
    }

    /// <summary>
    /// Deletes what it can and reports each top-level item through <paramref name="report"/>.
    /// Folders are emptied file by file, so one locked file no longer keeps a whole folder.
    /// </summary>
    public static CleanSummary Clean(string folder, bool keepRecent, Action<CleanItem> report, CleanProgress progress, CancellationToken token)
    {
        EnsureIsTempFolder(folder);
        var root = new DirectoryInfo(folder);
        if (!root.Exists)
            return new CleanSummary(0, 0, 0, Stopped: false);

        var run = new Run(DateTime.UtcNow - RecentWindow, keepRecent, progress, token);
        int deleted = 0, left = 0;
        long freed = 0;
        foreach (var entry in root.GetFileSystemInfos("*", OneLevel))
        {
            if (token.IsCancellationRequested)
                break;
            var tally = new Tally();
            if (entry is DirectoryInfo dir && !IsLink(dir))
                run.CleanFolder(dir, tally);
            else
                run.Delete(entry, tally);

            deleted += tally.Deleted;
            left += tally.Left;
            freed += tally.BytesFreed;
            report(new CleanItem(entry.Name, entry is DirectoryInfo, tally.Outcome, tally.BytesFreed, tally.Deleted, tally.Left));
        }
        return new CleanSummary(deleted, left, freed, token.IsCancellationRequested);
    }

    private static bool IsRecent(FileSystemInfo entry, DateTime cutoffUtc) =>
        entry.LastWriteTimeUtc > cutoffUtc || entry.CreationTimeUtc > cutoffUtc;

    private static bool IsLink(FileSystemInfo entry) => entry.Attributes.HasFlag(FileAttributes.ReparsePoint);

    /// <summary>A last guard against a misconfigured path: never empty a drive or a profile folder.</summary>
    private static void EnsureIsTempFolder(string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var protectedFolders = new[]
        {
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.MyDocuments,
        };
        bool tooBroad = string.Equals(full, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? ""), StringComparison.OrdinalIgnoreCase)
            || protectedFolders.Any(f => string.Equals(full, Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(f)), StringComparison.OrdinalIgnoreCase));
        if (tooBroad)
            throw new InvalidOperationException($"{full} isn’t a temp folder, so Housekeeper won’t clean it.");
    }

    private sealed class Tally
    {
        public int Deleted;
        public int Left;
        public long BytesFreed;
        public bool InUse, NoAccess, Recent;

        public CleanOutcome Outcome =>
            Left == 0 ? CleanOutcome.Deleted
            : Deleted > 0 ? CleanOutcome.Partly
            : InUse ? CleanOutcome.InUse
            : NoAccess ? CleanOutcome.NoAccess
            : CleanOutcome.Recent;
    }

    private sealed class Run(DateTime cutoffUtc, bool keepRecent, CleanProgress progress, CancellationToken token)
    {
        public void CleanFolder(DirectoryInfo dir, Tally tally)
        {
            FileSystemInfo[] children;
            try
            {
                children = dir.GetFileSystemInfos("*", OneLevel);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                tally.NoAccess = true;
                tally.Left++;
                return;
            }

            int leftBefore = tally.Left;
            foreach (var child in children)
            {
                if (token.IsCancellationRequested)
                    return;
                if (child is DirectoryInfo sub && !IsLink(sub))
                    CleanFolder(sub, tally);
                else
                    Delete(child, tally);
            }

            // Something inside stayed, so the folder has to stay too.
            if (tally.Left > leftBefore)
                return;
            if (keepRecent && dir.CreationTimeUtc > cutoffUtc)
            {
                tally.Recent = true;
                tally.Left++;
                return;
            }
            Remove(dir, tally, length: 0, isFile: false);
        }

        public void Delete(FileSystemInfo entry, Tally tally)
        {
            long length = entry is FileInfo file ? file.Length : 0;
            if (keepRecent && IsRecent(entry, cutoffUtc))
            {
                tally.Recent = true;
                tally.Left++;
                return;
            }
            Remove(entry, tally, length, isFile: entry is FileInfo);
        }

        private void Remove(FileSystemInfo entry, Tally tally, long length, bool isFile)
        {
            try
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReadOnly))
                    entry.Attributes &= ~FileAttributes.ReadOnly;
                entry.Delete();
                if (!isFile)
                    return;
                tally.Deleted++;
                tally.BytesFreed += length;
                progress.Add(length);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Already gone.
            }
            catch (IOException)
            {
                tally.InUse = true;
                tally.Left++;
            }
            catch (UnauthorizedAccessException)
            {
                tally.NoAccess = true;
                tally.Left++;
            }
        }
    }
}
