using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SlopClean.Platform.Windows;

internal static class DriverPackageIdentity
{
    public const string FileName = "driver-identity.json";

    public sealed record Document(
        string PublishedName,
        string PackageFingerprint,
        string ClassGuid,
        string Provider,
        IReadOnlyDictionary<string, string> FileHashes);

    public static Document Create(
        string publishedName,
        string packageFingerprint,
        string classGuid,
        string provider,
        string packageDirectory)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(packageDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(packageDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(packageDirectory, file);
                hashes[relative] = HashFile(file);
            }
        }

        return new Document(publishedName, packageFingerprint, classGuid, provider, hashes);
    }

    public static void Write(string packageDirectory, Document document)
    {
        Directory.CreateDirectory(packageDirectory);
        var path = Path.Combine(packageDirectory, FileName);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    public static Document? Read(string packageDirectory)
    {
        var path = Path.Combine(packageDirectory, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Document>(File.ReadAllText(path));
    }

    public static bool Verify(string packageDirectory, Document expected)
    {
        var actual = Create(
            expected.PublishedName,
            expected.PackageFingerprint,
            expected.ClassGuid,
            expected.Provider,
            packageDirectory);

        if (!string.Equals(actual.PackageFingerprint, expected.PackageFingerprint, StringComparison.Ordinal)
            || !string.Equals(actual.PublishedName, expected.PublishedName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (expected.FileHashes.Count == 0)
        {
            return false;
        }

        foreach (var pair in expected.FileHashes)
        {
            if (pair.Key.Equals(FileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!actual.FileHashes.TryGetValue(pair.Key, out var hash)
                || !string.Equals(hash, pair.Value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
