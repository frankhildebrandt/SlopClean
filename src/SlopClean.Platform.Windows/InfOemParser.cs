using System.Text.RegularExpressions;

namespace SlopClean.Platform.Windows;

public static partial class InfOemParser
{
    [GeneratedRegex(@"^\s*Provider\s*=\s*%?(?<v>[^%\r\n]+)%?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProviderRegex();

    [GeneratedRegex(@"^\s*ClassGUID\s*=\s*\{?(?<v>[0-9A-Fa-f-]{36})\}?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClassGuidRegex();

    [GeneratedRegex(@"^\s*DriverVer\s*=\s*(?<v>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DriverVerRegex();

    [GeneratedRegex(@"^\s*(?<k>[^;=]+)\s*=\s*(?<v>.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex StringKvRegex();

    public static bool TryRead(string infPath, out ParsedOemInf parsed)
    {
        parsed = default!;
        try
        {
            var lines = File.ReadAllLines(infPath);
            string? providerToken = null;
            Guid classGuid = Guid.Empty;
            string? driverVer = null;
            var inStrings = false;
            var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

                var pm = ProviderRegex().Match(line);
                if (pm.Success)
                {
                    providerToken = pm.Groups["v"].Value.Trim().Trim('"');
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
                    driverVer = dm.Groups["v"].Value.Trim();
                }
            }

            var provider = providerToken ?? "Unknown";
            if (strings.TryGetValue(provider, out var resolved))
            {
                provider = resolved;
            }

            var fileName = Path.GetFileName(infPath);
            var fingerprint = $"{fileName}|{provider}|{classGuid:D}|{driverVer ?? "?"}|{new FileInfo(infPath).Length}";
            parsed = new ParsedOemInf(fileName, Path.GetFileNameWithoutExtension(fileName) + ".inf", provider, classGuid, fingerprint);
            // Original name often equals published for oem; better from Catalog - keep published as file name.
            parsed = parsed with { OriginalName = fileName };
            return classGuid != Guid.Empty;
        }
        catch
        {
            return false;
        }
    }

    public sealed record ParsedOemInf(
        string PublishedName,
        string OriginalName,
        string Provider,
        Guid ClassGuid,
        string PackageFingerprint);
}
