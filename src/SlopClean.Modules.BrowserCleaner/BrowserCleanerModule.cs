using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;
using SlopClean.Core.Safety;

namespace SlopClean.Modules.BrowserCleaner;

public sealed class BrowserCleanerModule : IScannableModule, IApplicableModule
{
    public const string ModuleId = "browser-cleaner";

    private static readonly (string Id, string Process, string RelativeCache)[] Browsers =
    [
        ("chrome", "chrome", @"Google\Chrome\User Data\Default\Cache"),
        ("edge", "msedge", @"Microsoft\Edge\User Data\Default\Cache"),
        ("firefox", "firefox", @"Mozilla\Firefox\Profiles")
    ];

    private readonly IFileSystem _fileSystem;
    private readonly IProcessInspector _processes;
    private readonly SafetyPolicy _safetyPolicy;
    private readonly BoolParameter _includeCache;
    private readonly BoolParameter _includeCookies;
    private readonly BoolParameter _includeHistory;

    public BrowserCleanerModule(IFileSystem fileSystem, IProcessInspector processes, SafetyPolicy safetyPolicy)
    {
        _fileSystem = fileSystem;
        _processes = processes;
        _safetyPolicy = safetyPolicy;
        _includeCache = new BoolParameter("IncludeCache", "Cache", "Include browser cache files.", true);
        _includeCookies = new BoolParameter("IncludeCookies", "Cookies", "Include cookie databases (signed-out sites).", false);
        _includeHistory = new BoolParameter("IncludeHistory", "History", "Include browsing history databases.", false);
    }

    public string Id => ModuleId;
    public string Name => "Browser Cleaner";
    public string Description => "Cleans browser cache and optionally cookies/history.";
    public ModuleCategory Category => ModuleCategory.Browser;
    public IReadOnlyList<IModuleParameter> Parameters => [_includeCache, _includeCookies, _includeHistory];

    public async IAsyncEnumerable<ScanFinding> ScanAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IProgress<ScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var includeCache = _includeCache.Resolve(parameters);
        var includeCookies = _includeCookies.Resolve(parameters);
        var includeHistory = _includeHistory.Resolve(parameters);
        var localAppData = PathCanonicalizer.Canonicalize(_fileSystem.GetFolderPath(SpecialFolderKind.LocalApplicationData));
        var roaming = PathCanonicalizer.Canonicalize(_fileSystem.GetFolderPath(SpecialFolderKind.ApplicationData));
        var completed = 0;

        foreach (var browser in Browsers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            completed++;
            progress?.Report(new ScanProgress(ModuleId, $"Checking {browser.Id}", completed, Browsers.Length));

            if (_processes.IsProcessRunning(browser.Process))
            {
                yield return new ScanFinding(
                    Id: $"{ModuleId}:{browser.Id}:running",
                    ModuleId: ModuleId,
                    TargetId: browser.Id,
                    DisplayName: $"{browser.Id} is running",
                    Path: null,
                    SizeBytes: 0,
                    Risk: FindingRisk.Informational,
                    Details: "Close the browser before cleaning locked databases.",
                    IsActionable: false,
                    RequiredPrivilege: RequiredPrivilege.None,
                    AllowedRoot: null);
                continue;
            }

            if (includeCache)
            {
                await foreach (var finding in EmitCacheAsync(browser.Id, localAppData, roaming, browser.RelativeCache, cancellationToken)
                                   .ConfigureAwait(false))
                {
                    yield return finding;
                }
            }

            if (includeCookies)
            {
                foreach (var finding in EmitNamedFile(browser.Id, localAppData, roaming, "Cookies", "cookies"))
                {
                    yield return finding;
                }
            }

            if (includeHistory)
            {
                foreach (var finding in EmitNamedFile(browser.Id, localAppData, roaming, "History", "history"))
                {
                    yield return finding;
                }
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

        if (string.IsNullOrWhiteSpace(action.Path))
        {
            return Task.FromResult(ApplyResult.Skipped(action.Id, action.FindingId, "Missing path."));
        }

        try
        {
            if (_fileSystem.FileExists(action.Path))
            {
                var info = _fileSystem.GetFileInfo(action.Path);
                _fileSystem.DeleteFile(action.Path);
                return Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, info?.Length ?? 0, "Browser file deleted."));
            }

            if (_fileSystem.DirectoryExists(action.Path))
            {
                var size = _fileSystem.GetDirectorySize(action.Path, cancellationToken);
                foreach (var file in _fileSystem.EnumerateFiles(action.Path, "*", SearchOption.AllDirectories).ToArray())
                {
                    try { _fileSystem.DeleteFile(file); }
                    catch { /* skip locked */ }
                }

                return Task.FromResult(ApplyResult.Succeeded(action.Id, action.FindingId, size, "Browser cache cleaned."));
            }

            return Task.FromResult(ApplyResult.Skipped(action.Id, action.FindingId, "Target no longer exists."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ApplyResult.Failed(action.Id, action.FindingId, ex.Message));
        }
    }

    private async IAsyncEnumerable<ScanFinding> EmitCacheAsync(
        string browserId,
        string localAppData,
        string roaming,
        string relative,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var candidates = new[]
        {
            Path.Combine(localAppData, relative),
            Path.Combine(roaming, relative)
        };

        foreach (var candidate in candidates.Where(_fileSystem.DirectoryExists))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (browserId == "firefox")
            {
                foreach (var profileCache in _fileSystem.EnumerateDirectories(candidate, "*", SearchOption.TopDirectoryOnly)
                             .Select(p => Path.Combine(p, "cache2"))
                             .Where(_fileSystem.DirectoryExists))
                {
                    yield return CreateDirFinding(browserId, profileCache, localAppData, roaming);
                    await Task.Yield();
                }
            }
            else
            {
                yield return CreateDirFinding(browserId, candidate, localAppData, roaming);
                await Task.Yield();
            }
        }
    }

    private IEnumerable<ScanFinding> EmitNamedFile(
        string browserId,
        string localAppData,
        string roaming,
        string fileName,
        string targetSuffix)
    {
        var roots = new[]
        {
            Path.Combine(localAppData, browserId == "firefox" ? @"Mozilla\Firefox\Profiles" : GetUserData(browserId)),
            Path.Combine(roaming, browserId == "firefox" ? @"Mozilla\Firefox\Profiles" : GetUserData(browserId))
        };

        foreach (var root in roots.Where(_fileSystem.DirectoryExists))
        {
            foreach (var file in _fileSystem.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
            {
                var info = _fileSystem.GetFileInfo(file);
                if (info is null || info.IsReparsePoint)
                {
                    continue;
                }

                var allowedRoot = file.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase) ? localAppData : roaming;
                var validation = _safetyPolicy.ValidateDeletePath(file, allowedRoot);
                if (!validation.IsAllowed)
                {
                    continue;
                }

                yield return new ScanFinding(
                    Id: StableId(ModuleId, browserId, targetSuffix, file),
                    ModuleId: ModuleId,
                    TargetId: $"{browserId}-{targetSuffix}",
                    DisplayName: $"{browserId} {fileName}",
                    Path: file,
                    SizeBytes: info.Length,
                    Risk: FindingRisk.Medium,
                    Details: file,
                    IsActionable: true,
                    RequiredPrivilege: RequiredPrivilege.None,
                    AllowedRoot: allowedRoot,
                    Metadata: new Dictionary<string, string>
                    {
                        [OptimizationAction.OperationCodeMetadataKey] = PrivilegedOperationCodes.DeleteFile
                    });
            }
        }
    }

    private ScanFinding CreateDirFinding(string browserId, string path, string localAppData, string roaming)
    {
        var allowedRoot = path.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase) ? localAppData : roaming;
        var size = _fileSystem.GetDirectorySize(path);
        return new ScanFinding(
            Id: StableId(ModuleId, browserId, "cache", path),
            ModuleId: ModuleId,
            TargetId: $"{browserId}-cache",
            DisplayName: $"{browserId} cache",
            Path: path,
            SizeBytes: size,
            Risk: FindingRisk.Low,
            Details: path,
            IsActionable: true,
            RequiredPrivilege: RequiredPrivilege.None,
            AllowedRoot: allowedRoot,
            Metadata: new Dictionary<string, string>
            {
                [OptimizationAction.OperationCodeMetadataKey] = PrivilegedOperationCodes.DeleteDirectory
            });
    }

    private static string GetUserData(string browserId) => browserId switch
    {
        "chrome" => @"Google\Chrome\User Data\Default",
        "edge" => @"Microsoft\Edge\User Data\Default",
        _ => browserId
    };

    private static string StableId(params string[] parts)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
