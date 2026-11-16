using System.Security.Cryptography;
using System.Text;

namespace Housekeeper.Core;

/// <summary>Where the app keeps its files and which temp folder it cleans.</summary>
internal static class AppPaths
{
    private const string InstanceId = "Housekeeper-7E3A1C52";

    public static string DataDirectory { get; private set; } = "";
    public static string TempDirectory { get; private set; } = "";
    public static string InstanceName { get; private set; } = InstanceId;

    /// <summary>
    /// Sandbox mode (<c>--sandbox &lt;folder&gt;</c>) keeps settings and the "temp folder" inside the
    /// given folder and never touches the registry or the power state. Used for trying things out.
    /// </summary>
    public static bool IsSandbox { get; private set; }

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string ActivityLogFile => Path.Combine(DataDirectory, "activity.log");

    public static void Initialize(string? sandboxDirectory)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (sandboxDirectory is null)
        {
            DataDirectory = Path.Combine(localAppData, "Housekeeper");
            TempDirectory = Path.Combine(localAppData, "Temp");
        }
        else
        {
            IsSandbox = true;
            DataDirectory = Path.GetFullPath(sandboxDirectory);
            TempDirectory = Path.Combine(DataDirectory, "temp");
            InstanceName = InstanceId + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DataDirectory.ToUpperInvariant())))[..12];
            Directory.CreateDirectory(TempDirectory);
        }
        Directory.CreateDirectory(DataDirectory);
    }
}

internal static class AppInfo
{
    public const string Author = "agenth1tman";
    public const string AuthorUrl = "https://github.com/agenth1tman";

    /// <summary>"2.0" for version 2.0.0.</summary>
    public static readonly string Version = ReadVersion();

    private static string ReadVersion()
    {
        var version = typeof(AppInfo).Assembly.GetName().Version;
        return version is null ? "2.0" : $"{version.Major}.{version.Minor}";
    }
}

internal sealed record LaunchOptions(bool StartedWithWindows, string? SandboxDirectory)
{
    public static LaunchOptions Parse(string[] args)
    {
        bool startup = false;
        string? sandbox = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].TrimStart('-', '/').ToLowerInvariant())
            {
                // Version 1 registered itself with a bare "startup", so accept that spelling too.
                case "startup":
                    startup = true;
                    break;
                case "sandbox" when i + 1 < args.Length:
                    sandbox = args[++i];
                    break;
            }
        }
        return new LaunchOptions(startup, sandbox);
    }
}

/// <summary>
/// Makes sure only one copy runs. A second launch asks the first copy to show its window.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showRequest;
    private readonly RegisteredWaitHandle? _listener;

    public SingleInstance(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, @"Local\" + name, out bool createdNew);
        IsFirst = createdNew;
        _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + name + ".Show");
        if (IsFirst)
            _listener = ThreadPool.RegisterWaitForSingleObject(_showRequest, (_, _) => ActivationRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public bool IsFirst { get; }

    /// <summary>Raised on a thread-pool thread when another launch wants the window shown.</summary>
    public event Action? ActivationRequested;

    public void ActivateFirst()
    {
        // The new launch owns the foreground right now; pass that on so the window can come to the front.
        Ui.Native.AllowSetForegroundWindow(Ui.Native.AnyProcess);
        _showRequest.Set();
    }

    public void Dispose()
    {
        _listener?.Unregister(null);
        if (IsFirst)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
        _showRequest.Dispose();
    }
}
