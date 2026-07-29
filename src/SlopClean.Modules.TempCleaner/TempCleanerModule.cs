using System.Runtime.CompilerServices;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using PrivilegedOperationCodes = SlopClean.Core.Abstractions.PrivilegedOperationCodes;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;
using SlopClean.Core.Safety;

namespace SlopClean.Modules.TempCleaner;

public sealed class TempCleanerModule : IScannableModule, IApplicableModule, IModuleIllustration
{
    public const string ModuleId = "temp-cleaner";

    private readonly IFileSystem _fileSystem;
    private readonly SafetyPolicy _safetyPolicy;
    private readonly BoolParameter _includeWindowsTemp;
    private readonly BoolParameter _includeUserTemp;
    private readonly IntParameter _minAgeDays;

    public TempCleanerModule(IFileSystem fileSystem, SafetyPolicy safetyPolicy)
    {
        _fileSystem = fileSystem;
        _safetyPolicy = safetyPolicy;
        _includeWindowsTemp = new BoolParameter(
            "IncludeWindowsTemp",
            "Windows Temp",
            "Include files under the Windows Temp directory.",
            defaultValue: true);
        _includeUserTemp = new BoolParameter(
            "IncludeUserTemp",
            "User Temp",
            "Include files under the current user Temp directory.",
            defaultValue: true);
        _minAgeDays = new IntParameter(
            "MinAgeDays",
            "Minimum age (days)",
            "Only include files older than this many days.",
            defaultValue: 0,
            min: 0,
            max: 3650);
    }

    public string Id => ModuleId;
    public string Name => "Temp Cleaner";
    public string Description => "Finds and removes safe temporary files.";
    public ModuleCategory Category => ModuleCategory.Cleanup;
    public IReadOnlyList<IModuleParameter> Parameters => [_includeWindowsTemp, _includeUserTemp, _minAgeDays];

    public Stream OpenIllustration() => EmbeddedResourceStreams.OpenModuleIllustration(typeof(TempCleanerModule));

    public async IAsyncEnumerable<ScanFinding> ScanAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IProgress<ScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var includeWindows = _includeWindowsTemp.Resolve(parameters);
        var includeUser = _includeUserTemp.Resolve(parameters);
        var minAgeDays = _minAgeDays.Resolve(parameters);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-minAgeDays);
        var completed = 0;

        if (includeUser)
        {
            var userTemp = PathCanonicalizer.Canonicalize(_fileSystem.GetFolderPath(SpecialFolderKind.UserTemp));
            await foreach (var finding in ScanDirectoryAsync(userTemp, "user-temp", cutoff, progress, () => Interlocked.Increment(ref completed), cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return finding;
            }
        }

        if (includeWindows)
        {
            var windowsTemp = PathCanonicalizer.Canonicalize(_fileSystem.GetFolderPath(SpecialFolderKind.WindowsTemp));
            await foreach (var finding in ScanDirectoryAsync(windowsTemp, "windows-temp", cutoff, progress, () => Interlocked.Increment(ref completed), cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return finding;
            }
        }
    }

    public Task<ApplyResult> ApplyAsync(OptimizationAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = _safetyPolicy.ValidateAction(action);
        if (!validation.IsAllowed)
        {
            return Task.FromResult(ApplyResult.Skipped(action.Id, action.FindingId, validation.Reason ?? "Blocked."));
        }

        if (string.IsNullOrWhiteSpace(action.Path) || !_fileSystem.FileExists(action.Path))
        {
            return Task.FromResult(ApplyResult.Skipped(action.Id, action.FindingId, "File no longer exists."));
        }

        try
        {
            var info = _fileSystem.GetFileInfo(action.Path);
            _fileSystem.DeleteFile(action.Path);
            return Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, info?.Length ?? 0, "Temporary file deleted."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ApplyResult.Failed(action.Id, action.FindingId, ex.Message));
        }
    }

    private async IAsyncEnumerable<ScanFinding> ScanDirectoryAsync(
        string root,
        string targetId,
        DateTimeOffset cutoff,
        IProgress<ScanProgress>? progress,
        Func<int> nextCompleted,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_fileSystem.DirectoryExists(root))
        {
            yield break;
        }

        foreach (var file in _fileSystem.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = nextCompleted();
            progress?.Report(new ScanProgress(ModuleId, $"Scanning {targetId}", count));

            FileEntryInfo? info;
            try
            {
                info = _fileSystem.GetFileInfo(file);
            }
            catch
            {
                continue;
            }

            if (info is null || info.IsReparsePoint || info.LastWriteTimeUtc > cutoff)
            {
                continue;
            }

            var validation = _safetyPolicy.ValidateDeletePath(info.FullPath, root);
            if (!validation.IsAllowed)
            {
                continue;
            }

            yield return new ScanFinding(
                Id: $"{ModuleId}:{Guid.NewGuid():N}",
                ModuleId: ModuleId,
                TargetId: targetId,
                DisplayName: Path.GetFileName(info.FullPath),
                Path: info.FullPath,
                SizeBytes: info.Length,
                Risk: FindingRisk.Low,
                Details: $"Last write (UTC): {info.LastWriteTimeUtc:u}",
                IsActionable: true,
                RequiredPrivilege: targetId == "windows-temp" ? RequiredPrivilege.Elevated : RequiredPrivilege.None,
                AllowedRoot: root,
                Metadata: new Dictionary<string, string>
                {
                    [OptimizationAction.OperationCodeMetadataKey] = PrivilegedOperationCodes.DeleteFile
                });

            await Task.Yield();
        }
    }
}
