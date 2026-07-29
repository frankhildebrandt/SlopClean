using System.Globalization;
using System.Text.RegularExpressions;

namespace SlopClean.Platform.Windows;

public static partial class InfOemParser
{
    [GeneratedRegex(@"^\s*Provider\s*=\s*%?(?<v>[^%\r\n]+)%?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProviderRegex();

    [GeneratedRegex(@"^\s*ClassGUID\s*=\s*\{?(?<v>[0-9A-Fa-f-]{36})\}?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClassGuidRegex();

    [GeneratedRegex(@"^\s*Class\s*=\s*%?(?<v>[^%\r\n]+)%?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClassRegex();

    [GeneratedRegex(@"^\s*DriverVer\s*=\s*(?<v>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DriverVerRegex();

    [GeneratedRegex(@"^\s*(?<k>[^;=]+)\s*=\s*(?<v>.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex StringKvRegex();

    [GeneratedRegex(@"\b(?<file>[A-Za-z0-9][A-Za-z0-9._-]*\.sys)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SysFileRegex();

    public static bool TryRead(string infPath, out ParsedOemInf parsed)
    {
        parsed = default!;
        try
        {
            var lines = File.ReadAllLines(infPath);
            string? providerToken = null;
            string? classToken = null;
            Guid classGuid = Guid.Empty;
            string? driverVerRaw = null;
            var inStrings = false;
            var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in lines)
            {
                var line = raw.Split(';', 2)[0].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inStrings = line.Equals("[Strings]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inStrings)
                {
                    var sm = StringKvRegex().Match(line);
                    if (sm.Success)
                    {
                        strings[sm.Groups["k"].Value.Trim()] = sm.Groups["v"].Value.Trim().Trim('"');
                    }

                    continue;
                }

                foreach (Match sys in SysFileRegex().Matches(line))
                {
                    images.Add(sys.Groups["file"].Value);
                }

                var pm = ProviderRegex().Match(line);
                if (pm.Success)
                {
                    providerToken = pm.Groups["v"].Value.Trim().Trim('"');
                    continue;
                }

                var classMatch = ClassRegex().Match(line);
                if (classMatch.Success)
                {
                    classToken = classMatch.Groups["v"].Value.Trim().Trim('"');
                    continue;
                }

                var cm = ClassGuidRegex().Match(line);
                if (cm.Success && Guid.TryParse(cm.Groups["v"].Value, out var g))
                {
                    classGuid = g;
                    continue;
                }

                var dm = DriverVerRegex().Match(line);
                if (dm.Success)
                {
                    driverVerRaw = dm.Groups["v"].Value.Trim();
                }
            }

            var provider = ResolveString(providerToken, strings) ?? "Unknown";
            var className = ResolveString(classToken, strings);
            ParseDriverVer(driverVerRaw, out var driverDate, out var driverVersion);

            var fileName = Path.GetFileName(infPath);
            DateTimeOffset? lastWrite = null;
            long length = 0;
            try
            {
                var info = new FileInfo(infPath);
                length = info.Length;
                lastWrite = info.LastWriteTimeUtc;
            }
            catch
            {
                // ignore
            }

            var fingerprint = $"{fileName}|{provider}|{classGuid:D}|{driverVerRaw ?? "?"}|{length}";
            parsed = new ParsedOemInf(
                PublishedName: fileName,
                OriginalName: fileName,
                Provider: provider,
                ClassGuid: classGuid,
                PackageFingerprint: fingerprint,
                ClassName: className,
                DriverVersion: driverVersion,
                DriverDate: driverDate,
                InfLastWriteUtc: lastWrite,
                ApproximateSizeBytes: length,
                ReferencedImageFileNames: images.OrderBy(static n => n, StringComparer.OrdinalIgnoreCase).ToArray());
            return classGuid != Guid.Empty;
        }
        catch
        {
            return false;
        }
    }

    internal static void ParseDriverVer(string? raw, out DateOnly? date, out string? version)
    {
        date = null;
        version = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var parts = raw.Split(',', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1
            && DateOnly.TryParseExact(
                parts[0],
                ["M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
        {
            date = parsedDate;
        }

        if (parts.Length >= 2)
        {
            version = parts[1];
        }
        else if (date is null)
        {
            version = raw;
        }
    }

    private static string? ResolveString(string? token, IReadOnlyDictionary<string, string> strings)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return strings.TryGetValue(token, out var resolved) ? resolved : token;
    }

    public sealed record ParsedOemInf(
        string PublishedName,
        string OriginalName,
        string Provider,
        Guid ClassGuid,
        string PackageFingerprint,
        string? ClassName,
        string? DriverVersion,
        DateOnly? DriverDate,
        DateTimeOffset? InfLastWriteUtc,
        long ApproximateSizeBytes,
        IReadOnlyList<string> ReferencedImageFileNames);
}
