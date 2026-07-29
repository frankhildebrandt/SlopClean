using SlopClean.Core.Abstractions;

namespace SlopClean.Core.Tests.Fakes;

public sealed class FakeFileSystem : IFileSystem
{
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ReparsePoints { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> FileSizes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> FileContents { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<SpecialFolderKind, string> Folders { get; } = new();

    public bool DirectoryExists(string path) => Directories.Contains(Path.GetFullPath(path));
    public bool FileExists(string path) => Files.Contains(Path.GetFullPath(path));

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption option)
    {
        var root = NormalizeRoot(path);
        return Files.Where(f =>
        {
            if (!f.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (option == SearchOption.AllDirectories)
            {
                return true;
            }

            var relative = f[root.Length..];
            return !relative.Contains(Path.DirectorySeparatorChar) && !relative.Contains(Path.AltDirectorySeparatorChar);
        });
    }

    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption option)
    {
        var full = Path.GetFullPath(path);
        var root = NormalizeRoot(path);
        return Directories.Where(d =>
        {
            if (string.Equals(d, full, StringComparison.OrdinalIgnoreCase)
                || !d.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (option == SearchOption.AllDirectories)
            {
                return true;
            }

            var relative = d[root.Length..];
            return !relative.Contains(Path.DirectorySeparatorChar) && !relative.Contains(Path.AltDirectorySeparatorChar);
        });
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
        FileContents.Remove(full);
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        var full = Path.GetFullPath(path);
        if (recursive)
        {
            foreach (var file in Files.Where(f => f.StartsWith(full, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                Files.Remove(file);
                FileSizes.Remove(file);
                FileContents.Remove(file);
            }

            foreach (var dir in Directories.Where(d => d.StartsWith(full, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                Directories.Remove(dir);
            }
        }
        else
        {
            Directories.Remove(full);
        }
    }

    public void CreateDirectory(string path) => EnsureDirectory(path);

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite = true)
    {
        var source = Path.GetFullPath(sourcePath);
        var dest = Path.GetFullPath(destinationPath);
        if (!Files.Contains(source))
        {
            throw new FileNotFoundException(source);
        }

        if (Files.Contains(dest) && !overwrite)
        {
            throw new IOException("Destination exists.");
        }

        EnsureDirectory(Path.GetDirectoryName(dest)!);
        Files.Add(dest);
        FileSizes[dest] = FileSizes.GetValueOrDefault(source);
        if (FileContents.TryGetValue(source, out var content))
        {
            FileContents[dest] = content;
        }
    }

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = true)
    {
        CopyFile(sourcePath, destinationPath, overwrite);
        DeleteFile(sourcePath);
    }

    public void WriteAllText(string path, string contents)
    {
        var full = Path.GetFullPath(path);
        EnsureDirectory(Path.GetDirectoryName(full)!);
        Files.Add(full);
        FileContents[full] = contents;
        FileSizes[full] = contents.Length;
    }

    public string ReadAllText(string path)
    {
        var full = Path.GetFullPath(path);
        if (!Files.Contains(full))
        {
            throw new FileNotFoundException(full);
        }

        return FileContents.GetValueOrDefault(full, string.Empty);
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
        EnsureDirectory(Path.GetDirectoryName(full)!);
    }

    public void AddDirectory(string path) => EnsureDirectory(path);

    private void EnsureDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(full))
        {
            Directories.Add(full);
            var parent = Path.GetDirectoryName(full);
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, full, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            full = parent;
        }
    }

    private static string NormalizeRoot(string path)
        => Path.GetFullPath(path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
}
