using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;
using SlopClean.Core.Safety;

namespace SlopClean.Modules;

public sealed class UninstallCleanupModule : IScannableModule, IApplicableModule
{
    public const string ModuleId = "uninstall-cleanup";

    private static readonly string[] UninstallRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    private readonly IRegistryStore _registry;
    private readonly IFileSystem _fileSystem;
    private readonly SafetyPolicy _safetyPolicy;

    public UninstallCleanupModule(
        IRegistryStore registry,
        IFileSystem fileSystem,
        SafetyPolicy safetyPolicy)
    {
        _registry = registry;
        _fileSystem = fileSystem;
        _safetyPolicy = safetyPolicy;
    }

    public string Id => ModuleId;
    public string Name => "Uninstall Cleanup";
    public string Description => "Finds leftover uninstall registry entries and matching AppData folders after programs were removed.";
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

                    var orphaned = !string.IsNullOrWhiteSpace(uninstallString)
                                   && !uninstallExists
                                   && (string.IsNullOrWhiteSpace(installLocation) || !installExists);

                    if (!orphaned)
                    {
                        continue;
                    }

                    foreach (var leftover in BuildAppDataLeftovers(displayName, cancellationToken))
                    {
                        yield return leftover;
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
                        ["valueData"] = value.Data ?? "",
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

        if (action.OperationCode == PrivilegedOperationCodes.DeleteDirectory)
        {
            return ApplyDirectoryDeleteAsync(action, cancellationToken);
        }

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

    private Task<ApplyResult> ApplyDirectoryDeleteAsync(OptimizationAction action, CancellationToken cancellationToken)
    {
        var validation = _safetyPolicy.ValidateAction(action);
        if (!validation.IsAllowed)
        {
            return Task.FromResult(ApplyResult.Skipped(action.Id, action.FindingId, validation.Reason ?? "Blocked."));
        }

        if (string.IsNullOrWhiteSpace(action.Path) || !_fileSystem.DirectoryExists(action.Path))
        {
            return Task.FromResult(ApplyResult.Skipped(action.Id, action.FindingId, "Directory no longer exists."));
        }

        try
        {
            var size = _fileSystem.GetDirectorySize(action.Path, cancellationToken);
            _fileSystem.DeleteDirectory(action.Path, recursive: true);
            return Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, size, "AppData leftover folder deleted."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ApplyResult.Failed(action.Id, action.FindingId, ex.Message));
        }
    }

    private IEnumerable<ScanFinding> BuildAppDataLeftovers(string displayName, CancellationToken cancellationToken)
    {
        var safe = SanitizeName(displayName);
        if (string.IsNullOrWhiteSpace(safe) || safe is "." or "..")
        {
            yield break;
        }

        foreach (var (kind, elevated) in new[]
                 {
                     (SpecialFolderKind.LocalApplicationData, false),
                     (SpecialFolderKind.ApplicationData, false),
                     (SpecialFolderKind.CommonApplicationData, true)
                 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = PathCanonicalizer.Canonicalize(_fileSystem.GetFolderPath(kind));
            var candidate = PathCanonicalizer.Canonicalize(Path.Combine(root, safe));
            if (!_fileSystem.DirectoryExists(candidate))
            {
                continue;
            }

            var dirInfo = _fileSystem.GetDirectoryInfo(candidate);
            if (dirInfo?.IsReparsePoint == true)
            {
                continue;
            }

            var validation = _safetyPolicy.ValidateDeletePath(candidate, root);
            if (!validation.IsAllowed)
            {
                continue;
            }

            var size = _fileSystem.GetDirectorySize(candidate, cancellationToken);
            yield return new ScanFinding(
                Id: $"{ModuleId}:leftover:{kind}:{safe}",
                ModuleId: ModuleId,
                TargetId: "appdata-leftover",
                DisplayName: $"Leftover folder: {safe}",
                Path: candidate,
                SizeBytes: size,
                Risk: FindingRisk.Medium,
                Details: $"Matching AppData leftover for orphaned uninstall '{displayName}'. Size: {size} bytes. Select to delete.",
                IsActionable: true,
                RequiredPrivilege: elevated ? RequiredPrivilege.Elevated : RequiredPrivilege.None,
                AllowedRoot: root,
                Metadata: new Dictionary<string, string>
                {
                    ["displayName"] = displayName,
                    ["folderKind"] = kind.ToString(),
                    [OptimizationAction.OperationCodeMetadataKey] = PrivilegedOperationCodes.DeleteDirectory
                });
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
