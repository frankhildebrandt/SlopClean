using SlopClean.Core.Models;

namespace SlopClean.Modules.CoreIsolationDrivers;

public static class CoreIsolationDriverPackageMatcher
{
    public static List<OemDriverPackage> FindExactPackageOwners(
        IReadOnlyList<OemDriverPackage> packages,
        CodeIntegritySignal signal)
    {
        var haystack = $"{signal.ImageFileName}\n{signal.RawMessage}";
        var hits = new List<OemDriverPackage>();
        foreach (var package in packages)
        {
            if (MatchesPublishedOrOriginalName(package, haystack)
                || MatchesReferencedImage(package, signal.ImageFileName))
            {
                hits.Add(package);
            }
        }

        return hits;
    }

    private static bool MatchesPublishedOrOriginalName(OemDriverPackage package, string haystack)
    {
        if (haystack.Contains(package.PublishedName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(package.OriginalName)
               && !package.OriginalName.Equals(package.PublishedName, StringComparison.OrdinalIgnoreCase)
               && haystack.Contains(package.OriginalName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesReferencedImage(OemDriverPackage package, string? imageFileName)
    {
        if (string.IsNullOrWhiteSpace(imageFileName) || package.ImageFileNames.Count == 0)
        {
            return false;
        }

        return package.ImageFileNames.Any(image =>
            image.Equals(imageFileName, StringComparison.OrdinalIgnoreCase));
    }
}
