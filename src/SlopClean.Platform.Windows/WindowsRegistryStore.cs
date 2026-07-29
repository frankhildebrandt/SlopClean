using Microsoft.Win32;
using SlopClean.Core.Abstractions;

namespace SlopClean.Platform.Windows;

public sealed class WindowsRegistryStore : IRegistryStore
{
    public IReadOnlyList<RegistryValueInfo> GetValues(RegistryHiveKind hive, string subKey)
    {
        using var key = Open(hive, subKey, writable: false);
        if (key is null)
        {
            return [];
        }

        return key.GetValueNames()
            .Select(name => new RegistryValueInfo(
                name,
                key.GetValue(name)?.ToString(),
                key.GetValueKind(name).ToString()))
            .ToArray();
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHiveKind hive, string subKey)
    {
        using var key = Open(hive, subKey, writable: false);
        return key?.GetSubKeyNames() ?? [];
    }

    public string? GetStringValue(RegistryHiveKind hive, string subKey, string valueName)
    {
        using var key = Open(hive, subKey, writable: false);
        return key?.GetValue(valueName)?.ToString();
    }

    public void DeleteValue(RegistryHiveKind hive, string subKey, string valueName)
    {
        using var key = Open(hive, subKey, writable: true)
            ?? throw new InvalidOperationException($"Registry key '{subKey}' was not found.");
        key.DeleteValue(valueName, throwOnMissingValue: false);
    }

    public void DeleteSubKeyTree(RegistryHiveKind hive, string subKey)
    {
        using var root = GetHive(hive);
        root.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
    }

    public void SetStringValue(RegistryHiveKind hive, string subKey, string valueName, string value)
    {
        using var key = Open(hive, subKey, writable: true, create: true)
            ?? throw new InvalidOperationException($"Unable to open registry key '{subKey}'.");
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public string ExportKey(RegistryHiveKind hive, string subKey, string destinationFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        var hiveName = hive == RegistryHiveKind.CurrentUser ? "HKEY_CURRENT_USER" : "HKEY_LOCAL_MACHINE";
        var full = $"{hiveName}\\{subKey}";

        // Use reg.exe for a faithful .reg export that reg import can restore.
        // Do not redirect stdout+stderr together with WaitForExit — that deadlocks when buffers fill.
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "reg.exe",
            Arguments = $"export \"{full}\" \"{destinationFile}\" /y",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = false
        };

        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start reg.exe.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Registry export failed: {error}");
        }

        return destinationFile;
    }

    private static RegistryKey? Open(RegistryHiveKind hive, string subKey, bool writable, bool create = false)
    {
        var root = GetHive(hive);
        return create
            ? root.CreateSubKey(subKey, writable)
            : root.OpenSubKey(subKey, writable);
    }

    private static RegistryKey GetHive(RegistryHiveKind hive) => hive switch
    {
        RegistryHiveKind.CurrentUser => Registry.CurrentUser,
        RegistryHiveKind.LocalMachine => Registry.LocalMachine,
        _ => throw new ArgumentOutOfRangeException(nameof(hive), hive, null)
    };
}
