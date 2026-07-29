using SlopClean.Core.Abstractions;

namespace SlopClean.Core.Tests.Fakes;

public sealed class FakeFileSystem : IFileSystem
{
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ReparsePoints { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> FileSizes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<SpecialFolderKind, string> Folders { get; } = new();

    public bool DirectoryExists(string path) => Directories.Contains(Path.GetFullPath(path));
    public bool FileExists(string path) => Files.Contains(Path.GetFullPath(path));

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption option)
    {
        var root = Path.GetFullPath(path).TrimEnd('\\') + "\\";
        return Files.Where(f => f.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption option)
    {
        var root = Path.GetFullPath(path).TrimEnd('\\') + "\\";
        return Directories.Where(d => d.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                                      !string.Equals(d, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
    }

    public FileEntryInfo? GetFileInfo(string path)
    {
        var full = Path.GetFullPath(path);
        if (!Files.Contains(full))
        {
            return null;
        }

        return new FileEntryInfo(full, FileSizes.GetValueOrDefault(full), DateTimeOffset.UtcNow.AddDays(-2), ReparsePoints.Contains(full));
    }

    public DirectoryEntryInfo? GetDirectoryInfo(string path)
    {
        var full = Path.GetFullPath(path);
        if (!Directories.Contains(full))
        {
            return null;
        }

        return new DirectoryEntryInfo(full, DateTimeOffset.UtcNow, ReparsePoints.Contains(full));
    }

    public long GetDirectorySize(string path, CancellationToken cancellationToken = default)
        => EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => FileSizes.GetValueOrDefault(f));

    public void DeleteFile(string path)
    {
        var full = Path.GetFullPath(path);
        Files.Remove(full);
        FileSizes.Remove(full);
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        var full = Path.GetFullPath(path);
        Directories.Remove(full);
        if (recursive)
        {
            foreach (var file in Files.Where(f => f.StartsWith(full, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                Files.Remove(file);
                FileSizes.Remove(file);
            }
        }
    }

    public string GetFullPath(string path) => Path.GetFullPath(path);
    public string GetTempPath() => GetFolderPath(SpecialFolderKind.UserTemp);
    public string GetFolderPath(SpecialFolderKind folder) => Folders[folder];
    public bool IsReparsePoint(string path) => ReparsePoints.Contains(Path.GetFullPath(path));
    public string? GetDriveRoot(string path) => Path.GetPathRoot(Path.GetFullPath(path));

    public void AddFile(string path, long size = 10)
    {
        var full = Path.GetFullPath(path);
        Files.Add(full);
        FileSizes[full] = size;
        Directories.Add(Path.GetDirectoryName(full)!);
    }

    public void AddDirectory(string path)
    {
        Directories.Add(Path.GetFullPath(path));
    }
}
