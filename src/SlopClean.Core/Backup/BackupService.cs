using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Safety;

namespace SlopClean.Core.Backup;

public sealed class BackupService : IBackupService
{
    private readonly IFileSystem _fileSystem;
    private readonly IRegistryStore _registry;
    private readonly IRestorePointStore _store;
    private readonly SafetyPolicy _safetyPolicy;

    public BackupService(
        IFileSystem fileSystem,
        IRegistryStore registry,
        IRestorePointStore store,
        SafetyPolicy safetyPolicy)
    {
        _fileSystem = fileSystem;
        _registry = registry;
        _store = store;
        _safetyPolicy = safetyPolicy;
    }

    public bool CanCreateBackup(OptimizationAction action)
        => action.OperationCode is PrivilegedOperationCodes.DeleteFile
            or PrivilegedOperationCodes.DeleteDirectory
            or PrivilegedOperationCodes.DeleteRegistryKey
            or PrivilegedOperationCodes.DeleteRegistryValue
            or PrivilegedOperationCodes.DisableStartupShortcut;

    public async Task<RestorePointManifest?> CreatePendingBackupAsync(
        OptimizationAction action,
        string? displayName = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanCreateBackup(action))
        {
            return null;
        }

        var manifest = new RestorePointManifest
        {
            Id = Guid.NewGuid().ToString("N"),
            ModuleId = action.ModuleId,
            ActionId = action.Id,
            FindingId = action.FindingId,
            DisplayName = displayName ?? action.Path ?? action.FindingId,
            OperationCode = action.OperationCode,
            RequiredPrivilege = action.RequiredPrivilege,
            OriginalPath = action.Path,
            AllowedRoot = action.AllowedRoot,
            Metadata = action.Payload?.ToDictionary(static kv => kv.Key, static kv => kv.Value)
                       ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        await _store.CreatePendingAsync(manifest, cancellationToken).ConfigureAwait(false);
        var payloadRoot = _store.GetPayloadDirectory(manifest.Id);

        try
        {
            switch (action.OperationCode)
            {
                case PrivilegedOperationCodes.DeleteFile:
                    BackupFile(action.Path!, payloadRoot, manifest);
                    break;
                case PrivilegedOperationCodes.DeleteDirectory:
                case PrivilegedOperationCodes.DisableStartupShortcut when _fileSystem.DirectoryExists(action.Path!):
                    BackupDirectory(action.Path!, payloadRoot, manifest, cancellationToken);
                    break;
                case PrivilegedOperationCodes.DisableStartupShortcut:
                    BackupFile(action.Path!, payloadRoot, manifest);
                    manifest.Kind = RestorePointKind.StartupShortcut;
                    break;
                case PrivilegedOperationCodes.DeleteRegistryKey:
                    BackupRegistryKey(action, payloadRoot, manifest);
                    break;
                case PrivilegedOperationCodes.DeleteRegistryValue:
                    BackupRegistryValue(action, payloadRoot, manifest);
                    break;
                default:
                    await _store.DiscardAsync(manifest.Id, cancellationToken).ConfigureAwait(false);
                    return null;
            }

            await _store.SaveAsync(manifest, cancellationToken).ConfigureAwait(false);
            return manifest;
        }
        catch
        {
            await _store.DiscardAsync(manifest.Id, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ApplyResult> RestoreAsync(string restoreId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = await _store.GetAsync(restoreId, cancellationToken).ConfigureAwait(false);
        if (manifest is null)
        {
            return ApplyResult.Failed(restoreId, restoreId, "Restore point not found.");
        }

        if (manifest.Status != RestorePointStatus.Committed)
        {
            return ApplyResult.Failed(manifest.ActionId, restoreId, $"Restore point is {manifest.Status}, not committed.");
        }

        try
        {
            switch (manifest.Kind)
            {
                case RestorePointKind.File:
                case RestorePointKind.StartupShortcut:
                    RestoreFile(manifest);
                    break;
                case RestorePointKind.Directory:
                    RestoreDirectory(manifest);
                    break;
                case RestorePointKind.RegistryExport:
                    RestoreRegistryExport(manifest);
                    break;
                case RestorePointKind.RegistryValue:
                    RestoreRegistryValue(manifest);
                    break;
                default:
                    return ApplyResult.Failed(manifest.ActionId, restoreId, $"Unsupported restore kind '{manifest.Kind}'.");
            }

            await _store.MarkRestoredAsync(restoreId, cancellationToken).ConfigureAwait(false);
            return ApplyResult.Succeeded(manifest.ActionId, restoreId, 0, "Restore completed.");
        }
        catch (Exception ex)
        {
            await _store.MarkFailedAsync(restoreId, cancellationToken).ConfigureAwait(false);
            return ApplyResult.Failed(manifest.ActionId, restoreId, ex.Message);
        }
    }

    private void BackupFile(string path, string payloadRoot, RestorePointManifest manifest)
    {
        if (!_fileSystem.FileExists(path))
        {
            throw new InvalidOperationException($"File '{path}' no longer exists.");
        }

        if (_fileSystem.IsReparsePoint(path))
        {
            throw new InvalidOperationException("Refusing to back up a reparse point.");
        }

        var fileName = Path.GetFileName(path);
        var destination = Path.Combine(payloadRoot, fileName);
        _fileSystem.CopyFile(path, destination);
        manifest.Kind = RestorePointKind.File;
        manifest.PayloadRelativePath = fileName;
        manifest.OriginalPath = PathCanonicalizer.Canonicalize(path);
    }

    private void BackupDirectory(
        string path,
        string payloadRoot,
        RestorePointManifest manifest,
        CancellationToken cancellationToken)
    {
        if (!_fileSystem.DirectoryExists(path))
        {
            throw new InvalidOperationException($"Directory '{path}' no longer exists.");
        }

        if (_fileSystem.IsReparsePoint(path))
        {
            throw new InvalidOperationException("Refusing to back up a reparse point.");
        }

        var canonical = PathCanonicalizer.Canonicalize(path);
        var folderName = Path.GetFileName(canonical.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var destinationRoot = Path.Combine(payloadRoot, folderName);
        _fileSystem.CreateDirectory(destinationRoot);

        foreach (var dir in _fileSystem.EnumerateDirectories(canonical, "*", SearchOption.AllDirectories).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_fileSystem.IsReparsePoint(dir))
            {
                continue;
            }

            var relative = Path.GetRelativePath(canonical, dir);
            _fileSystem.CreateDirectory(Path.Combine(destinationRoot, relative));
        }

        foreach (var file in _fileSystem.EnumerateFiles(canonical, "*", SearchOption.AllDirectories).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_fileSystem.IsReparsePoint(file))
            {
                continue;
            }

            var relative = Path.GetRelativePath(canonical, file);
            var dest = Path.Combine(destinationRoot, relative);
            _fileSystem.CreateDirectory(Path.GetDirectoryName(dest)!);
            _fileSystem.CopyFile(file, dest);
        }

        manifest.Kind = RestorePointKind.Directory;
        manifest.PayloadRelativePath = folderName;
        manifest.OriginalPath = canonical;
    }

    private void BackupRegistryKey(OptimizationAction action, string payloadRoot, RestorePointManifest manifest)
    {
        if (action.Payload is null
            || !action.Payload.TryGetValue("hive", out var hiveText)
            || !action.Payload.TryGetValue("subKey", out var subKey))
        {
            throw new InvalidOperationException("Registry key payload is incomplete.");
        }

        var exportPath = Path.Combine(payloadRoot, "backup.reg");
        var hive = Enum.Parse<RegistryHiveKind>(hiveText, true);
        _registry.ExportKey(hive, subKey, exportPath);
        manifest.Kind = RestorePointKind.RegistryExport;
        manifest.PayloadRelativePath = "backup.reg";
        manifest.Metadata["hive"] = hiveText;
        manifest.Metadata["subKey"] = subKey;
    }

    private void BackupRegistryValue(OptimizationAction action, string payloadRoot, RestorePointManifest manifest)
    {
        if (action.Payload is null
            || !action.Payload.TryGetValue("hive", out var hiveText)
            || !action.Payload.TryGetValue("subKey", out var subKey)
            || !action.Payload.TryGetValue("valueName", out var valueName))
        {
            throw new InvalidOperationException("Registry value payload is incomplete.");
        }

        var hive = Enum.Parse<RegistryHiveKind>(hiveText, true);
        var valueData = action.Payload.TryGetValue("valueData", out var fromPayload)
            ? fromPayload
            : _registry.GetStringValue(hive, subKey, valueName) ?? string.Empty;

        manifest.Kind = RestorePointKind.RegistryValue;
        manifest.Metadata["hive"] = hiveText;
        manifest.Metadata["subKey"] = subKey;
        manifest.Metadata["valueName"] = valueName;
        manifest.Metadata["valueData"] = valueData;

        // Also export the parent key when possible for richer restore.
        try
        {
            var exportPath = Path.Combine(payloadRoot, "backup.reg");
            _registry.ExportKey(hive, subKey, exportPath);
            manifest.PayloadRelativePath = "backup.reg";
        }
        catch
        {
            // Value metadata alone is enough for string Run values.
        }
    }

    private void RestoreFile(RestorePointManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.OriginalPath)
            || string.IsNullOrWhiteSpace(manifest.PayloadRelativePath))
        {
            throw new InvalidOperationException("File restore metadata is incomplete.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.AllowedRoot))
        {
            var validation = _safetyPolicy.ValidateDeletePath(manifest.OriginalPath, manifest.AllowedRoot);
            if (!validation.IsAllowed)
            {
                throw new InvalidOperationException(validation.Reason ?? "Restore path blocked by safety policy.");
            }
        }

        var source = Path.Combine(_store.GetPayloadDirectory(manifest.Id), manifest.PayloadRelativePath);
        if (!_fileSystem.FileExists(source))
        {
            throw new InvalidOperationException("Backup payload file is missing.");
        }

        _fileSystem.CreateDirectory(Path.GetDirectoryName(manifest.OriginalPath)!);
        _fileSystem.CopyFile(source, manifest.OriginalPath);
    }

    private void RestoreDirectory(RestorePointManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.OriginalPath)
            || string.IsNullOrWhiteSpace(manifest.PayloadRelativePath))
        {
            throw new InvalidOperationException("Directory restore metadata is incomplete.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.AllowedRoot))
        {
            var validation = _safetyPolicy.ValidateDeletePath(manifest.OriginalPath, manifest.AllowedRoot);
            if (!validation.IsAllowed)
            {
                throw new InvalidOperationException(validation.Reason ?? "Restore path blocked by safety policy.");
            }
        }

        var sourceRoot = Path.Combine(_store.GetPayloadDirectory(manifest.Id), manifest.PayloadRelativePath);
        if (!_fileSystem.DirectoryExists(sourceRoot))
        {
            throw new InvalidOperationException("Backup payload directory is missing.");
        }

        _fileSystem.CreateDirectory(manifest.OriginalPath);
        foreach (var dir in _fileSystem.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories).ToArray())
        {
            var relative = Path.GetRelativePath(sourceRoot, dir);
            _fileSystem.CreateDirectory(Path.Combine(manifest.OriginalPath, relative));
        }

        foreach (var file in _fileSystem.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToArray())
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            var dest = Path.Combine(manifest.OriginalPath, relative);
            _fileSystem.CreateDirectory(Path.GetDirectoryName(dest)!);
            _fileSystem.CopyFile(file, dest);
        }
    }

    private void RestoreRegistryExport(RestorePointManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.PayloadRelativePath))
        {
            throw new InvalidOperationException("Registry export payload is missing.");
        }

        var exportPath = Path.Combine(_store.GetPayloadDirectory(manifest.Id), manifest.PayloadRelativePath);
        if (!_fileSystem.FileExists(exportPath))
        {
            throw new InvalidOperationException("Backup .reg file is missing.");
        }

        // Prefer value-level restore when metadata is available.
        if (manifest.Metadata.ContainsKey("valueName")
            && manifest.Metadata.ContainsKey("valueData"))
        {
            RestoreRegistryValue(manifest);
            return;
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
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
        }
    }

    private void RestoreRegistryValue(RestorePointManifest manifest)
    {
        var hive = Enum.Parse<RegistryHiveKind>(manifest.Metadata["hive"], true);
        _registry.SetStringValue(
            hive,
            manifest.Metadata["subKey"],
            manifest.Metadata["valueName"],
            manifest.Metadata.GetValueOrDefault("valueData") ?? string.Empty);
    }
}
