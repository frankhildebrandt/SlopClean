namespace SlopClean.Core.Safety;

public static class PathCanonicalizer
{
    public static string Canonicalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool IsUnderRoot(string path, string root)
    {
        var canonicalPath = Canonicalize(path) + Path.DirectorySeparatorChar;
        var canonicalRoot = Canonicalize(root) + Path.DirectorySeparatorChar;
        return canonicalPath.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetDriveRoot(string path)
    {
        var root = Path.GetPathRoot(Canonicalize(path));
        return string.IsNullOrWhiteSpace(root) ? null : Canonicalize(root);
    }
}
