using System.Text.RegularExpressions;

namespace SlopClean.Platform.Windows;

public static partial class DriverPackageNameResolver
{
    [GeneratedRegex(@"^oem\d+\.inf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OemInfNameRegex();

    public static string? ResolvePublishedOemName(
        string? driverNodeInfPath,
        string? deviceDriverInfPath,
        IReadOnlyDictionary<string, string> originalNameToPublished)
    {
        if (TryOemFileName(driverNodeInfPath, out var fromNode))
        {
            return fromNode;
        }

        if (TryOemFileName(deviceDriverInfPath, out var fromDevice))
        {
            return fromDevice;
        }

        var original = FileName(deviceDriverInfPath) ?? FileName(driverNodeInfPath);
        if (original is not null
            && originalNameToPublished.TryGetValue(original, out var published)
            && TryOemFileName(published, out var mapped))
        {
            return mapped;
        }

        return null;
    }

    private static bool TryOemFileName(string? pathOrName, out string oemName)
    {
        oemName = FileName(pathOrName) ?? string.Empty;
        return OemInfNameRegex().IsMatch(oemName);
    }

    private static string? FileName(string? pathOrName)
    {
        if (string.IsNullOrWhiteSpace(pathOrName))
        {
            return null;
        }

        var name = Path.GetFileName(pathOrName.Trim().Trim('"'));
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
