using System.Globalization;

namespace Housekeeper.Core;

/// <summary>How numbers, sizes and dates read on screen (always in the user's own culture).</summary>
internal static class Format
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    public static string Bytes(long bytes)
    {
        if (bytes < 1024)
            return bytes == 1 ? "1 byte" : $"{bytes.ToString("N0", Culture)} bytes";
        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = -1;
        do
        {
            value /= 1024;
            unit++;
        }
        while (value >= 1024 && unit < units.Length - 1);
        var digits = value >= 100 ? "N0" : value >= 10 ? "N1" : "N2";
        return $"{value.ToString(digits, Culture)} {units[unit]}";
    }

    public static string Rate(double bytesPerSecond) =>
        bytesPerSecond < 1024 * 1024
            ? $"{(bytesPerSecond / 1024).ToString("N0", Culture)} KB/s"
            : $"{(bytesPerSecond / 1024 / 1024).ToString("N1", Culture)} MB/s";

    /// <summary>"1 file", "2,341 files".</summary>
    public static string Count(int count, string one, string many) =>
        $"{count.ToString("N0", Culture)} {(count == 1 ? one : many)}";

    /// <summary>"today", "yesterday", or "Mon 9 Nov".</summary>
    public static string Day(DateTime date) => (date.Date - DateTime.Today).Days switch
    {
        0 => "today",
        -1 => "yesterday",
        1 => "tomorrow",
        _ => date.ToString("ddd d MMM", Culture),
    };

    public static string Time(DateTime time) => time.ToString("t", Culture);

    /// <summary>"4:05" or "1:02:30".</summary>
    public static string Clock(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }
}
