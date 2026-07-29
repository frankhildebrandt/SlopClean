using SlopClean.Core.Abstractions;

namespace SlopClean.Core.Tests.Fakes;

public sealed class FakeRegistryStore : IRegistryStore
{
    private readonly IFileSystem _fileSystem;

    public FakeRegistryStore(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public Dictionary<(RegistryHiveKind Hive, string SubKey), List<string>> SubKeys { get; } = new();
    public Dictionary<(RegistryHiveKind Hive, string SubKey, string Name), string> StringValues { get; } = new();

    public IReadOnlyList<RegistryValueInfo> GetValues(RegistryHiveKind hive, string subKey)
        => StringValues
            .Where(kv => kv.Key.Hive == hive && kv.Key.SubKey == subKey)
            .Select(kv => new RegistryValueInfo(kv.Key.Name, kv.Value, "String"))
            .ToArray();

    public IReadOnlyList<string> GetSubKeyNames(RegistryHiveKind hive, string subKey)
        => SubKeys.TryGetValue((hive, subKey), out var list) ? list : [];

    public string? GetStringValue(RegistryHiveKind hive, string subKey, string valueName)
        => StringValues.TryGetValue((hive, subKey, valueName), out var value) ? value : null;

    public void DeleteValue(RegistryHiveKind hive, string subKey, string valueName)
        => StringValues.Remove((hive, subKey, valueName));

    public void DeleteSubKeyTree(RegistryHiveKind hive, string subKey)
    {
        foreach (var key in StringValues.Keys
                     .Where(k => k.Hive == hive && k.SubKey.StartsWith(subKey, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            StringValues.Remove(key);
        }
    }

    public void SetStringValue(RegistryHiveKind hive, string subKey, string valueName, string value)
        => StringValues[(hive, subKey, valueName)] = value;

    public string ExportKey(RegistryHiveKind hive, string subKey, string destinationFile)
    {
        _fileSystem.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        _fileSystem.WriteAllText(destinationFile, $"Windows Registry Editor Version 5.00{Environment.NewLine}; {hive}\\{subKey}");
        return destinationFile;
    }
}
