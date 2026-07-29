namespace SlopClean.Core.Abstractions;

public static class PrivilegedOperationCodes
{
    public const string DeleteFile = "delete-file";
    public const string DeleteDirectory = "delete-directory";
    public const string EmptyRecycleBin = "empty-recycle-bin";
    public const string DeleteRegistryValue = "delete-registry-value";
    public const string DeleteRegistryKey = "delete-registry-key";
    public const string DisableStartupShortcut = "disable-startup-shortcut";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        DeleteFile,
        DeleteDirectory,
        EmptyRecycleBin,
        DeleteRegistryValue,
        DeleteRegistryKey,
        DisableStartupShortcut
    };
}
