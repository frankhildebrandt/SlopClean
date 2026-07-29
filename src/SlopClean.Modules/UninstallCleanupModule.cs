using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;

namespace SlopClean.Modules;

public sealed class UninstallCleanupModule : IScannableModule, IReversibleModule
{
    public const string ModuleId = "uninstall-cleanup";

    private static readonly string[] UninstallRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    private readonly IRegistryStore _registry;
    private readonly IFileSystem _fileSystem;
    private readonly string _backupDirectory;

    public UninstallCleanupModule(IRegistryStore registry, IFileSystem fileSystem, string? backupDirectory = null)
    {
        _registry = registry;
        _fileSystem = fileSystem;
        _backupDirectory = backupDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SlopClean",
                "registry-backups");
    }

    public string Id => ModuleId;
    public string Name => "Uninstall Cleanup";
    public string Description => "Finds leftover uninstall registry entries after programs were removed.";
    public ModuleCategory Category => ModuleCategory.Uninstall;
    public IReadOnlyList<IModuleParameter> Parameters => [];

    public async IAsyncEnumerable<ScanFinding> ScanAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IProgress<ScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var completed = 0;
        foreach (var hive in new[] { RegistryHiveKind.CurrentUser, RegistryHiveKind.LocalMachine })
        {
            foreach (var root in UninstallRoots)
            {
                foreach (var subName in _registry.GetSubKeyNames(hive, root))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    completed++;
                    progress?.Report(new ScanProgress(ModuleId, "Scanning uninstall keys", completed));

                    var subKey = $"{root}\\{subName}";
                    var displayName = _registry.GetStringValue(hive, subKey, "DisplayName") ?? subName;
                    var uninstallString = _registry.GetStringValue(hive, subKey, "UninstallString");
                    var installLocation = _registry.GetStringValue(hive, subKey, "InstallLocation");
                    var systemComponent = _registry.GetStringValue(hive, subKey, "SystemComponent");
                    var windowsInstaller = _registry.GetStringValue(hive, subKey, "WindowsInstaller");
                    var releaseType = _registry.GetStringValue(hive, subKey, "ReleaseType");
                    var bundleProvider = _registry.GetStringValue(hive, subKey, "BundleProviderKey");

                    // Conservative exclusions.
                    if (systemComponent == "1"
                        || windowsInstaller == "1"
                        || !string.IsNullOrWhiteSpace(bundleProvider)
                        || string.Equals(releaseType, "Update", StringComparison.OrdinalIgnoreCase)
                        || subName.StartsWith("KB", StringComparison.OrdinalIgnoreCase)
                        || LooksLikeProductCode(subName))
                    {
                        continue;
                    }

                    var uninstallPath = ExtractPath(uninstallString);
                    var installExists = !string.IsNullOrWhiteSpace(installLocation)
                        && _fileSystem.DirectoryExists(installLocation);
                    var uninstallExists = !string.IsNullOrWhiteSpace(uninstallPath)
                        && (_fileSystem.FileExists(uninstallPath) || _fileSystem.DirectoryExists(uninstallPath));

                    // Require BOTH install location and uninstall binary to be missing when install location was recorded.
                    var orphaned = !string.IsNullOrWhiteSpace(uninstallString)
                                   && !uninstallExists
                                   && (string.IsNullOrWhiteSpace(installLocation) || !installExists);

                    if (!orphaned)
                    {
                        continue;
                    }

                    // Informational AppData hints — never auto-deletable.
                    foreach (var hint in BuildAppDataHints(displayName))
                    {
                        yield return hint;
                    }

                    yield return new ScanFinding(
                        Id: $"{ModuleId}:{hive}:{subName}",
                        ModuleId: ModuleId,
                        TargetId: "orphaned-uninstall",
                        DisplayName: displayName,
                        Path: null,
                        SizeBytes: 0,
                        Risk: FindingRisk.Medium,
                        Details: $"Orphaned uninstall entry: {hive}\\{subKey}",
                        IsActionable: true,
                        RequiredPrivilege: hive == RegistryHiveKind.LocalMachine
                            ? RequiredPrivilege.Elevated
                            : RequiredPrivilege.None,
                        AllowedRoot: null,
                        Metadata: new Dictionary<string, string>
                        {
                            ["hive"] = hive.ToString(),
                            ["subKey"] = subKey,
                            ["displayName"] = displayName,
                            [OptimizationAction.OperationCodeMetadataKey] = PrivilegedOperationCodes.DeleteRegistryKey
                        });
                }
            }
        }

        // Dead Run values whose target executable is gone.
        const string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        foreach (var hive in new[] { RegistryHiveKind.CurrentUser, RegistryHiveKind.LocalMachine })
        {
            foreach (var value in _registry.GetValues(hive, runKey))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = ExtractPath(value.Data);
                if (string.IsNullOrWhiteSpace(path) || _fileSystem.FileExists(path))
                {
                    continue;
                }

                yield return new ScanFinding(
                    Id: $"{ModuleId}:run:{hive}:{value.Name}",
                    ModuleId: ModuleId,
                    TargetId: "dead-run",
                    DisplayName: value.Name,
                    Path: null,
                    SizeBytes: 0,
                    Risk: FindingRisk.Medium,
                    Details: $"Run entry points to missing file: {value.Data}",
                    IsActionable: true,
                    RequiredPrivilege: hive == RegistryHiveKind.LocalMachine
                        ? RequiredPrivilege.Elevated
                        : RequiredPrivilege.None,
                    AllowedRoot: null,
                    Metadata: new Dictionary<string, string>
                    {
                        ["hive"] = hive.ToString(),
                        ["subKey"] = runKey,
                        ["valueName"] = value.Name,
                        ["kind"] = "value",
                        [OptimizationAction.OperationCodeMetadataKey] = PrivilegedOperationCodes.DeleteRegistryValue
                    });
            }
        }

        await Task.Yield();
    }

    public Task<ApplyResult> ApplyAsync(OptimizationAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (action.Payload is null)
            {
                return Task.FromResult(ApplyResult.Failed(action.Id, action.FindingId, "Missing registry payload."));
            }

            var hive = Enum.Parse<RegistryHiveKind>(action.Payload["hive"], true);
            var subKey = action.Payload["subKey"];
            if (action.Payload.TryGetValue("kind", out var kind) && kind == "value")
            {
                _registry.DeleteValue(hive, subKey, action.Payload["valueName"]);
                return Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, 0, "Dead Run value removed."));
            }

            _registry.DeleteSubKeyTree(hive, subKey);
            return Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, 0, "Orphaned uninstall key removed."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ApplyResult.Failed(action.Id, action.FindingId, ex.Message));
        }
    }

    public Task<RestoreToken> CreateRestoreAsync(OptimizationAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_backupDirectory);
        var hive = Enum.Parse<RegistryHiveKind>(action.Payload!["hive"], true);
        var subKey = action.Payload["subKey"];
        var exportPath = Path.Combine(_backupDirectory, $"{Guid.NewGuid():N}.reg");
        _registry.ExportKey(hive, subKey, exportPath);

        var token = new RestoreToken(
            Id: Guid.NewGuid().ToString("N"),
            ModuleId: ModuleId,
            ActionId: action.Id,
            CreatedUtc: DateTimeOffset.UtcNow,
            Kind: "registry-export",
            Data: new Dictionary<string, string>
            {
                ["exportPath"] = exportPath,
                ["hive"] = hive.ToString(),
                ["subKey"] = subKey
            });

        File.WriteAllText(Path.Combine(_backupDirectory, $"{token.Id}.json"), JsonSerializer.Serialize(token));
        return Task.FromResult(token);
    }

    public Task<ApplyResult> RestoreAsync(RestoreToken token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!token.Data.TryGetValue("exportPath", out var exportPath) || !File.Exists(exportPath))
            {
                return Task.FromResult(ApplyResult.Failed(token.ActionId, token.Id, "Backup .reg file missing."));
            }

            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"import \"{exportPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            using var process = System.Diagnostics.Process.Start(start)
                ?? throw new InvalidOperationException("Failed to start reg.exe.");
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return Task.FromResult(ApplyResult.Failed(token.ActionId, token.Id, process.StandardError.ReadToEnd()));
            }

            return Task.FromResult(ApplyResult.Succeeded(token.ActionId, token.Id, 0, "Registry backup restored."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ApplyResult.Failed(token.ActionId, token.Id, ex.Message));
        }
    }

    private IEnumerable<ScanFinding> BuildAppDataHints(string displayName)
    {
        var safe = SanitizeName(displayName);
        if (string.IsNullOrWhiteSpace(safe))
        {
            yield break;
        }

        foreach (var kind in new[]
                 {
                     SpecialFolderKind.LocalApplicationData,
                     SpecialFolderKind.ApplicationData,
                     SpecialFolderKind.CommonApplicationData
                 })
        {
            var root = _fileSystem.GetFolderPath(kind);
            var candidate = Path.Combine(root, safe);
            if (_fileSystem.DirectoryExists(candidate))
            {
                yield return new ScanFinding(
                    Id: $"{ModuleId}:hint:{kind}:{safe}",
                    ModuleId: ModuleId,
                    TargetId: "appdata-hint",
                    DisplayName: $"Possible leftover folder: {safe}",
                    Path: candidate,
                    SizeBytes: _fileSystem.GetDirectorySize(candidate),
                    Risk: FindingRisk.Informational,
                    Details: "Shown for manual review only. SlopClean will not delete AppData leftovers automatically.",
                    IsActionable: false,
                    RequiredPrivilege: RequiredPrivilege.None,
                    AllowedRoot: root);
            }
        }
    }

    private static bool LooksLikeProductCode(string name)
        => Regex.IsMatch(name, @"^\{[0-9A-Fa-f\-]{36}\}$");

    private static string? ExtractPath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            if (end > 1)
            {
                return trimmed[1..end];
            }
        }

        var first = trimmed.Split(' ', 2)[0];
        return first;
    }

    private static string SanitizeName(string name)
        => Regex.Replace(name, @"[<>:""/\\|?*]", string.Empty).Trim();
}
