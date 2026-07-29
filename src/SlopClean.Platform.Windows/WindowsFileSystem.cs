using System.Runtime.InteropServices;
using SlopClean.Core.Abstractions;

namespace SlopClean.Platform.Windows;

public sealed class WindowsFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption option)
    {
        if (!Directory.Exists(path))
        {
            return [];
        }

        return Directory.EnumerateFiles(path, searchPattern, new EnumerationOptions
        {
            RecurseSubdirectories = option == SearchOption.AllDirectories,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        });
    }

    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption option)
    {
        if (!Directory.Exists(path))
        {
            return [];
        }

        return Directory.EnumerateDirectories(path, searchPattern, new EnumerationOptions
        {
            RecurseSubdirectories = option == SearchOption.AllDirectories,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        });
    }

    public FileEntryInfo? GetFileInfo(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        return new FileEntryInfo(
            info.FullName,
            info.Length,
            info.LastWriteTimeUtc,
            info.Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    public DirectoryEntryInfo? GetDirectoryInfo(string path)
    {
        if (!Directory.Exists(path))
        {
            return null;
        }

        var info = new DirectoryInfo(path);
        return new DirectoryEntryInfo(
            info.FullName,
            info.LastWriteTimeUtc,
            info.Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    public long GetDirectorySize(string path, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // Skip locked/inaccessible files.
            }
        }

        return total;
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive);
        }
    }

    public string GetFullPath(string path) => Path.GetFullPath(path);

    public string GetTempPath() => Path.GetTempPath();

    public string GetFolderPath(SpecialFolderKind folder) => folder switch
    {
        SpecialFolderKind.UserTemp => Path.GetTempPath(),
        SpecialFolderKind.WindowsTemp => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
        SpecialFolderKind.LocalApplicationData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        SpecialFolderKind.ApplicationData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        SpecialFolderKind.CommonApplicationData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        SpecialFolderKind.UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        SpecialFolderKind.Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        SpecialFolderKind.System => Environment.GetFolderPath(Environment.SpecialFolder.System),
        SpecialFolderKind.Startup => Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        SpecialFolderKind.CommonStartup => Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
        _ => throw new ArgumentOutOfRangeException(nameof(folder), folder, null)
    };

    public bool IsReparsePoint(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
            }

            if (Directory.Exists(path))
            {
                return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public string? GetDriveRoot(string path)
    {
        try
        {
            return Path.GetPathRoot(Path.GetFullPath(path));
        }
        catch
        {
            return null;
        }
    }
}
