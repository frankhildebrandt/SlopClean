using SlopClean.Core.Models;

namespace SlopClean.Modules.CoreIsolationDrivers;

internal static class CoreIsolationDriverPackageMatcher
{
    public static List<OemDriverPackage> FindExactPackageOwners(
        IReadOnlyList<OemDriverPackage> packages,
        CodeIntegritySignal signal)
    {
        var haystack = $"{signal.ImageFileName}\n{signal.RawMessage}";
        var hits = new List<OemDriverPackage>();
        foreach (var package in packages)
        {
            // Exact ownership tokens only — published OEM name or original INF name in the event text.
            if (haystack.Contains(package.PublishedName, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(package.OriginalName)
                    && !package.OriginalName.Equals(package.PublishedName, StringComparison.OrdinalIgnoreCase)
                    && haystack.Contains(package.OriginalName, StringComparison.OrdinalIgnoreCase)))
            {
                hits.Add(package);
            }
        }

        return hits;
    }
}
