namespace SlopClean.Core.Abstractions;

public interface IRegistryStore
{
    IReadOnlyList<RegistryValueInfo> GetValues(RegistryHiveKind hive, string subKey);
    IReadOnlyList<string> GetSubKeyNames(RegistryHiveKind hive, string subKey);
    string? GetStringValue(RegistryHiveKind hive, string subKey, string valueName);
    void DeleteValue(RegistryHiveKind hive, string subKey, string valueName);
    void DeleteSubKeyTree(RegistryHiveKind hive, string subKey);
    void SetStringValue(RegistryHiveKind hive, string subKey, string valueName, string value);
    string ExportKey(RegistryHiveKind hive, string subKey, string destinationFile);
}

public enum RegistryHiveKind
{
    CurrentUser,
    LocalMachine
}

public sealed record RegistryValueInfo(
    string Name,
    string? Data,
    string Kind);
