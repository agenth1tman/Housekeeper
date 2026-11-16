namespace Housekeeper.Core;

/// <summary>
/// When the automatic clean is due. It works from dates rather than a counter, so days the PC
/// was off still count, and a missed day can never break it.
/// </summary>
internal static class CleanSchedule
{
    /// <summary>The day the next automatic clean is due, or null when automatic cleaning is off.</summary>
    public static DateTime? NextClean(AppSettings settings)
    {
        if (!settings.AutoClean)
            return null;
        if (settings.AutoCleanFrom is not { } from)
            return DateTime.Today;
        return from.Date.AddDays(settings.AutoCleanDays);
    }

    public static bool IsDue(AppSettings settings, DateTime now) =>
        NextClean(settings) is { } next && now >= next;
}
