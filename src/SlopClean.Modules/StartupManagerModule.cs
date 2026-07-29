using System.Runtime.CompilerServices;
using System.Text.Json;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;

namespace SlopClean.Modules;

public sealed class StartupManagerModule : IScannableModule, IReversibleModule
{
    public const string ModuleId = "startup-manager";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private readonly IRegistryStore _registry;
    private readonly IFileSystem _fileSystem;
    private readonly string _stateDirectory;

    public StartupManagerModule(IRegistryStore registry, IFileSystem fileSystem, string? stateDirectory = null)
    {
        _registry = registry;
        _fileSystem = fileSystem;
        _stateDirectory = stateDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SlopClean",
                "startup-state");
    }

    public string Id => ModuleId;
    public string Name => "Startup Manager";
    public string Description => "Lists startup entries and allows enabling/disabling them.";
    public ModuleCategory Category => ModuleCategory.Startup;
    public IReadOnlyList<IModuleParameter> Parameters => [];

    public async IAsyncEnumerable<ScanFinding> ScanAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IProgress<ScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var completed = 0;
        foreach (var hive in new[] { RegistryHiveKind.CurrentUser, RegistryHiveKind.LocalMachine })
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var value in _registry.GetValues(hive, RunKey))
            {
                completed++;
                progress?.Report(new ScanProgress(ModuleId, "Reading Run keys", completed));
                yield return new ScanFinding(
                    Id: $"{ModuleId}:run:{hive}:{value.Name}",
                    ModuleId: ModuleId,
                    TargetId: "run-key",
                    DisplayName: value.Name,
                    Path: null,
                    SizeBytes: 0,
                    Risk: FindingRisk.Medium,
                    Details: $"{hive}\\{RunKey} = {value.Data}",
                    IsActionable: true,
                    RequiredPrivilege: hive == RegistryHiveKind.LocalMachine
                        ? RequiredPrivilege.Elevated
                        : RequiredPrivilege.None,
                    AllowedRoot: null,
                    Metadata: new Dictionary<string, string>
                    {
                        ["hive"] = hive.ToString(),
                        ["subKey"] = RunKey,
                        ["valueName"] = value.Name,
                        ["valueData"] = value.Data ?? "",
                        ["kind"] = "registry",
                        [OptimizationAction.OperationCodeMetadataKey] = PrivilegedOperationCodes.DeleteRegistryValue
                    });
            }
        }

        foreach (var folderKind in new[] { SpecialFolderKind.Startup, SpecialFolderKind.CommonStartup })
        {
            var folder = _fileSystem.GetFolderPath(folderKind);
            if (!_fileSystem.DirectoryExists(folder))
            {
                continue;
            }

            foreach (var file in _fileSystem.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                completed++;
                progress?.Report(new ScanProgress(ModuleId, "Reading Startup folder", completed));
                var info = _fileSystem.GetFileInfo(file);
                var needsElevation = folderKind == SpecialFolderKind.CommonStartup;
                yield return new ScanFinding(
                    Id: $"{ModuleId}:folder:{folderKind}:{Path.GetFileName(file)}",
                    ModuleId: ModuleId,
                    TargetId: "startup-folder",
                    DisplayName: Path.GetFileName(file),
                    Path: file,
                    SizeBytes: info?.Length ?? 0,
                    Risk: FindingRisk.Low,
                    Details: needsElevation
                        ? $"{file} (common startup; disable requires future elevated shortcut support)"
                        : file,
                    IsActionable: !needsElevation,
                    RequiredPrivilege: RequiredPrivilege.None,
                    AllowedRoot: folder,
                    Metadata: needsElevation
                        ? null
                        : new Dictionary<string, string>
                        {
                            ["kind"] = "shortcut",
                            ["disabledPath"] = Path.Combine(_stateDirectory, "disabled", Path.GetFileName(file)),
                            [OptimizationAction.OperationCodeMetadataKey] = PrivilegedOperationCodes.DisableStartupShortcut
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
                return Task.FromResult(ApplyResult.Failed(action.Id, action.FindingId, "Missing startup payload."));
            }

            if (action.Payload.TryGetValue("kind", out var kind) && kind == "registry")
            {
                var hive = Enum.Parse<RegistryHiveKind>(action.Payload["hive"], true);
                var subKey = action.Payload["subKey"];
                var valueName = action.Payload["valueName"];
                _registry.DeleteValue(hive, subKey, valueName);
                return Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, 0, "Startup registry entry disabled."));
            }

            if (string.IsNullOrWhiteSpace(action.Path) || !_fileSystem.FileExists(action.Path))
            {
                return Task.FromResult(ApplyResult.Skipped(action.Id, action.FindingId, "Startup shortcut no longer exists."));
            }

            var disabledDir = Path.Combine(_stateDirectory, "disabled");
            Directory.CreateDirectory(disabledDir);
            var destination = Path.Combine(disabledDir, Path.GetFileName(action.Path));
            File.Move(action.Path, destination, overwrite: true);
            return Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, 0, "Startup shortcut disabled."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ApplyResult.Failed(action.Id, action.FindingId, ex.Message));
        }
    }

    public Task<RestoreToken> CreateRestoreAsync(OptimizationAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_stateDirectory);
        var token = new RestoreToken(
            Id: Guid.NewGuid().ToString("N"),
            ModuleId: ModuleId,
            ActionId: action.Id,
            CreatedUtc: DateTimeOffset.UtcNow,
            Kind: "startup",
            Data: action.Payload?.ToDictionary(static kv => kv.Key, static kv => kv.Value)
                  ?? new Dictionary<string, string>());

        var path = Path.Combine(_stateDirectory, $"{token.Id}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(token));
        return Task.FromResult(token);
    }

    public Task<ApplyResult> RestoreAsync(RestoreToken token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (token.Data.TryGetValue("kind", out var kind) && kind == "registry")
            {
                var hive = Enum.Parse<RegistryHiveKind>(token.Data["hive"], true);
                _registry.SetStringValue(hive, token.Data["subKey"], token.Data["valueName"], token.Data["valueData"]);
                return Task.FromResult(ApplyResult.Succeeded(token.ActionId, token.Id, 0, "Startup registry entry restored."));
            }

            if (token.Data.TryGetValue("disabledPath", out var disabled)
                && token.Data.TryGetValue("originalPath", out var original)
                && File.Exists(disabled))
            {
                File.Move(disabled, original, overwrite: true);
                return Task.FromResult(ApplyResult.Succeeded(token.ActionId, token.Id, 0, "Startup shortcut restored."));
            }

            // For folder disables we stored disabledPath only; reconstruct original from allowed root metadata if present.
            if (token.Data.TryGetValue("disabledPath", out var disabledOnly) && File.Exists(disabledOnly))
            {
                var startup = _fileSystem.GetFolderPath(SpecialFolderKind.Startup);
                var destination = Path.Combine(startup, Path.GetFileName(disabledOnly));
                File.Move(disabledOnly, destination, overwrite: true);
                return Task.FromResult(ApplyResult.Succeeded(token.ActionId, token.Id, 0, "Startup shortcut restored."));
            }

            return Task.FromResult(ApplyResult.Failed(token.ActionId, token.Id, "Restore data incomplete."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ApplyResult.Failed(token.ActionId, token.Id, ex.Message));
        }
    }
}
