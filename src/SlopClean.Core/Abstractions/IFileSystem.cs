namespace SlopClean.Core.Abstractions;

public interface IFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption option);
    IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption option);
    FileEntryInfo? GetFileInfo(string path);
    DirectoryEntryInfo? GetDirectoryInfo(string path);
    long GetDirectorySize(string path, CancellationToken cancellationToken = default);
    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);
    string GetFullPath(string path);
    string GetTempPath();
    string GetFolderPath(SpecialFolderKind folder);
    bool IsReparsePoint(string path);
    string? GetDriveRoot(string path);
}

public sealed record FileEntryInfo(
    string FullPath,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    bool IsReparsePoint);

public sealed record DirectoryEntryInfo(
    string FullPath,
    DateTimeOffset LastWriteTimeUtc,
    bool IsReparsePoint);

public enum SpecialFolderKind
{
    UserTemp,
    WindowsTemp,
    LocalApplicationData,
    ApplicationData,
    CommonApplicationData,
    UserProfile,
    Windows,
    System,
    Startup,
    CommonStartup
}
