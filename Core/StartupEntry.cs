using System.Security;
using Microsoft.Win32;

namespace Housekeeper.Core;

/// <summary>The "open when Windows starts" entry under HKCU\…\Run.</summary>
internal static class StartupEntry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Housekeeper";
    private const string LegacyValueName = "VeryImportantApp";

    private static string ThisExe => Environment.ProcessPath ?? Application.ExecutablePath;

    /// <summary>The exe Windows starts, or null when there is no entry.</summary>
    public static string? RegisteredExe()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string command ? ExeFrom(command) : null;
    }

    public static bool IsThisExe(string? path) =>
        path is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(ThisExe), StringComparison.OrdinalIgnoreCase);

    /// <summary>Adds or removes the entry for this exe. Throws when Windows refuses.</summary>
    public static void Apply(bool enabled)
    {
        if (AppPaths.IsSandbox)
        {
            ActivityLog.Info($"Sandbox: the startup entry would have been {(enabled ? "added" : "removed")}.");
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
            key.SetValue(ValueName, $"\"{ThisExe}\" --startup");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        ActivityLog.Info(enabled ? $"Startup entry set to {ThisExe}." : "Startup entry removed.");
    }

    /// <summary>
    /// Run at every launch. Re-points the entry when the exe it names has gone (the app was moved
    /// or updated), and on the first run after an upgrade removes the entry version 1 left behind.
    /// An entry for another copy that still exists is left alone, so running a test build doesn't
    /// take over startup.
    /// </summary>
    public static void Repair(bool enabled, bool removeLegacyEntry)
    {
        if (AppPaths.IsSandbox)
            return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (removeLegacyEntry && key.GetValue(LegacyValueName) is string legacy
                && string.Equals(Path.GetFileName(ExeFrom(legacy)), "VeryImportantApp.exe", StringComparison.OrdinalIgnoreCase))
            {
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
                ActivityLog.Info($"Removed the startup entry of Very Important App ({legacy}).");
            }

            if (enabled && (key.GetValue(ValueName) is not string current || ExeFrom(current) is not { } exe || !File.Exists(exe)))
            {
                key.SetValue(ValueName, $"\"{ThisExe}\" --startup");
                ActivityLog.Info($"Startup entry set to {ThisExe}.");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            ActivityLog.Error("Couldn't check the startup entry.", ex);
        }
    }

    private static string? ExeFrom(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        int space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }
}
