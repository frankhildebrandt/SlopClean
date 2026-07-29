using System.Runtime.CompilerServices;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;

namespace SlopClean.Modules;

public sealed class StartupManagerModule : IScannableModule, IApplicableModule
{
    public const string ModuleId = "startup-manager";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private readonly IRegistryStore _registry;
    private readonly IFileSystem _fileSystem;

    public StartupManagerModule(IRegistryStore registry, IFileSystem fileSystem)
    {
        _registry = registry;
        _fileSystem = fileSystem;
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
                            ["originalPath"] = file,
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

            // Engine creates a file backup before apply; disable by removing the shortcut.
            _fileSystem.DeleteFile(action.Path);
            return Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, 0, "Startup shortcut disabled."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ApplyResult.Failed(action.Id, action.FindingId, ex.Message));
        }
    }
}
