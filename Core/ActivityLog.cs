using System.Globalization;

namespace Housekeeper.Core;

/// <summary>
/// A plain text record of what the app did and why (startups, automatic cleans, auto shutdown,
/// errors), kept in the data folder so problems like a schedule that never fires can be traced.
/// </summary>
internal static class ActivityLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} {exception.GetType().Name}: {exception.Message}");

    public static void Crash(Exception exception) => Write("CRASH", exception.ToString());

    private static void Write(string level, string message)
    {
        if (AppPaths.DataDirectory.Length == 0)
            return;
        try
        {
            lock (Gate)
            {
                var path = AppPaths.ActivityLogFile;
                var file = new FileInfo(path);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(path, Path.ChangeExtension(path, ".old.log"), overwrite: true);
                var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                File.AppendAllText(path, $"{stamp}  {level}  {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
