using System.Text.Json;
using System.Text.Json.Serialization;

namespace Housekeeper.Core;

internal enum ShutdownTrigger { DownloadsFinish, Timer }

internal enum PowerAction { ShutDown, Sleep }

/// <summary>
/// Everything the app remembers, saved as JSON in the data folder. Unlike version 1, the file
/// doesn't depend on where the exe lives, so moving or updating the app keeps the settings.
/// </summary>
internal sealed class AppSettings
{

    // Startup
    public bool OpenAtStartup { get; set; }
    public bool ExitAfterStartupCheck { get; set; }

    // Temp cleaner
    public bool AutoClean { get; set; }
    public int AutoCleanDays { get; set; } = 5;

    /// <summary>
    /// The moment the automatic-clean countdown started: the last clean, or when automatic
    /// cleaning was switched on. The next clean is due <see cref="AutoCleanDays"/> calendar days later.
    /// </summary>
    public DateTime? AutoCleanFrom { get; set; }

    public DateTime? LastCleaned { get; set; }
    public long LastCleanedBytes { get; set; }
    public int LastCleanedFiles { get; set; }
    public bool KeepRecentFiles { get; set; } = true;
    public bool ShowLog { get; set; } = true;

    // Auto shutdown
    public ShutdownTrigger ShutdownTrigger { get; set; }
    public bool WatchUploads { get; set; }
    public int IdleBelowKBps { get; set; } = 100;
    public int IdleMinutes { get; set; } = 5;
    public int TimerMinutes { get; set; } = 60;
    public PowerAction PowerAction { get; set; }

    // Window
    public bool MinimizeToTray { get; set; } = true;
    public string? LastPage { get; set; }
    public int[]? WindowBounds { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>True on the first run after taking over the settings of "Very Important App".</summary>
    [JsonIgnore]
    public bool ImportedFromVersion1 { get; set; }

    /// <summary>Raised after every save, so screens can show the new state.</summary>
    public event EventHandler? Saved;

    public static AppSettings Load()
    {
        var path = AppPaths.SettingsFile;
        if (File.Exists(path))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJson.Default.AppSettings) ?? new AppSettings();
                loaded.Normalize();
                return loaded;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                ActivityLog.Error("Couldn't read the settings file, so the defaults are used. The old file was kept as settings.json.bad.", ex);
                try { File.Move(path, path + ".bad", overwrite: true); } catch (IOException) { }
            }
        }

        var settings = LegacySettings.TryImport() ?? new AppSettings();
        settings.Normalize();
        settings.Save();
        return settings;
    }

    public void Save()
    {
        try
        {
            var path = AppPaths.SettingsFile;
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActivityLog.Error("Couldn't save settings.", ex);
        }
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void Normalize()
    {
        AutoCleanDays = Math.Clamp(AutoCleanDays, 1, 365);
        IdleBelowKBps = Math.Clamp(IdleBelowKBps, 1, 100_000);
        IdleMinutes = Math.Clamp(IdleMinutes, 1, 240);
        TimerMinutes = Math.Clamp(TimerMinutes, 1, 1440);

        // A clock that was set back must not push the next clean months into the future.
        var now = DateTime.Now;
        if (AutoCleanFrom > now.AddDays(1))
            AutoCleanFrom = now;
        if (LastCleaned > now.AddDays(1))
            LastCleaned = null;
    }
}

/// <summary>JSON code generated at build time, which loads much faster than reflection at startup.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
