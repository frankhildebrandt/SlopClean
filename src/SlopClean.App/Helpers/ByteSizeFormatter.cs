namespace SlopClean.App.Helpers;

internal static class ByteSizeFormatter
{
    /// <summary>
    /// Formats a byte count for UI. Returns empty when <paramref name="bytes"/> is 0
    /// so modules without a meaningful size (e.g. registry cleanup) stay clean.
    /// </summary>
    public static string Format(long bytes)
    {
        if (bytes <= 0)
        {
            return string.Empty;
        }

        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var i = 0;
        while (value >= 1024 && i < suffixes.Length - 1)
        {
            value /= 1024;
            i++;
        }

        return $"{value:0.##} {suffixes[i]}";
    }

    public static string FormatOrDash(long bytes)
    {
        var formatted = Format(bytes);
        return string.IsNullOrEmpty(formatted) ? "—" : formatted;
    }
}
