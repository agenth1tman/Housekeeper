using System.Globalization;
using System.Xml.Linq;

namespace Housekeeper.Core;

/// <summary>
/// Takes over the settings of version 1 ("Very Important App"), which kept them in a .NET
/// user.config under %LOCALAPPDATA%\h1tman.
/// </summary>
internal static class LegacySettings
{
    public static AppSettings? TryImport()
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "h1tman");
            if (!Directory.Exists(root))
                return null;

            var file = Directory.EnumerateDirectories(root, "VeryImportantApp*")
                .SelectMany(dir => Directory.EnumerateFiles(dir, "user.config", SearchOption.AllDirectories))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (file is null)
                return null;

            var values = XDocument.Load(file)
                .Descendants("setting")
                .Where(e => e.Attribute("name") is not null)
                .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? "");

            bool Flag(string name) => values.TryGetValue(name, out var v) && bool.TryParse(v, out var b) && b;
            float? Number(string name) => values.TryGetValue(name, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;

            int total = (int)Math.Clamp(Number("totalDays") ?? 5, 1, 365);
            var settings = new AppSettings
            {
                ImportedFromVersion1 = true,
                OpenAtStartup = Flag("openAtStartup"),
                // Version 1 only honoured this while "Enable Advanced Settings" was ticked.
                ExitAfterStartupCheck = Flag("exitAfterChecking") && Flag("enableAdvancedSettings"),
                AutoClean = Flag("enableAutoDelete"),
                AutoCleanDays = total,
                ShowLog = !values.ContainsKey("enableLog") || Flag("enableLog"),
                AutoCleanFrom = CountdownStart(Number("remainingDays"), total, values.GetValueOrDefault("previousCheckDate")),
            };

            ActivityLog.Info($"Imported settings from Very Important App ({file}). Automatic clean {(settings.AutoClean ? $"every {total} days, next due {CleanSchedule.NextClean(settings):d}" : "off")}.");
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException)
        {
            ActivityLog.Error("Couldn't import settings from Very Important App.", ex);
            return null;
        }
    }

    /// <summary>
    /// Version 1 counted "remaining days" down by one per day the app ran, and stopped working for
    /// good once the count slipped below zero. A sane count is turned into a start date; a broken
    /// one (zero, negative, or bigger than the interval) makes the next clean due straight away.
    /// </summary>
    private static DateTime? CountdownStart(float? remaining, int total, string? lastCheck)
    {
        if (remaining is not { } days || days <= 0 || days > total || string.IsNullOrEmpty(lastCheck))
            return null;
        if (!DateTime.TryParseExact(lastCheck, "M/d/yyyy", CultureInfo.CurrentCulture, DateTimeStyles.None, out var checkedOn)
            && !DateTime.TryParseExact(lastCheck, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out checkedOn))
            return null;
        return checkedOn.Date.AddDays((int)days - total);
    }
}
