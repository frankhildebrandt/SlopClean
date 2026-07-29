using System.Text.RegularExpressions;

namespace SlopClean.Platform.Windows;

public static partial class PnPUtilEnumDriversParser
{
    [GeneratedRegex(
        @"^\s*(Published Name|Veröffentlichter Name)\s*:\s*(?<v>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PublishedRegex();

    [GeneratedRegex(
        @"^\s*(Original Name|Originalname|Ursprünglicher Name)\s*:\s*(?<v>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OriginalRegex();

    public static IReadOnlyDictionary<string, string> ParseOriginalToPublished(string output)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? published = null;
        foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var publishedMatch = PublishedRegex().Match(raw);
            if (publishedMatch.Success)
            {
                published = publishedMatch.Groups["v"].Value.Trim();
                continue;
            }

            var originalMatch = OriginalRegex().Match(raw);
            if (!originalMatch.Success || string.IsNullOrWhiteSpace(published))
            {
                continue;
            }

            var original = originalMatch.Groups["v"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(original))
            {
                map[original] = published;
            }

            published = null;
        }

        return map;
    }

    public static IReadOnlyDictionary<string, string> ParsePublishedToOriginal(string output)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (original, published) in ParseOriginalToPublished(output))
        {
            map[published] = original;
        }

        return map;
    }
}
