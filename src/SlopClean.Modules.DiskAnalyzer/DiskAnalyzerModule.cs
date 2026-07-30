using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;
using SlopClean.Core.Safety;

namespace SlopClean.Modules.DiskAnalyzer;

public sealed class DiskAnalyzerModule : IScannableModule, IModuleIllustration
{
    public const string ModuleId = "disk-analyzer";
    public const string ModeLargestFiles = "LargestFiles";
    public const string ModeDuplicates = "Duplicates";

    private const int ProgressEveryFiles = 64;

    private readonly IFileSystem _fileSystem;
    private readonly EnumParameter _mode;
    private readonly PathListParameter _rootPath;
    private readonly IntParameter _topN;
    private readonly IntParameter _minSizeMb;

    public DiskAnalyzerModule(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
        _mode = new EnumParameter(
            "Mode",
            "Mode",
            "Largest files, or duplicate files (size then SHA-1).",
            ModeLargestFiles,
            [ModeLargestFiles, ModeDuplicates]);
        // Full drive roots are supported, but defaulting to the profile keeps MVP scans responsive.
        var defaultRoot = fileSystem.GetFolderPath(SpecialFolderKind.UserProfile);
        _rootPath = new PathListParameter(
            "RootPath",
            "Roots",
            "Drive or folder roots to analyze (one path per line).",
            [defaultRoot]);
        _topN = new IntParameter("TopN", "Top N", "Largest files to report, or max duplicate groups.", 50, 1, 500);
        _minSizeMb = new IntParameter("MinSizeMb", "Minimum size (MB)", "Ignore files smaller than this.", 50, 1, 1024 * 1024);
    }

    public string Id => ModuleId;
    public string Name => "Disk Analyzer";
    public string Description => "Finds the largest files or duplicate files (by size then SHA-1). Analysis only — no deletion.";
    public ModuleCategory Category => ModuleCategory.Analysis;
    public IReadOnlyList<IModuleParameter> Parameters => [_mode, _rootPath, _topN, _minSizeMb];

    public Stream OpenIllustration() => EmbeddedResourceStreams.OpenModuleIllustration(typeof(DiskAnalyzerModule));

    public async IAsyncEnumerable<ScanFinding> ScanAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IProgress<ScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var mode = _mode.Resolve(parameters);
        if (string.Equals(mode, ModeDuplicates, StringComparison.OrdinalIgnoreCase))
        {
            await foreach (var finding in ScanDuplicatesAsync(parameters, progress, cancellationToken).ConfigureAwait(false))
            {
                yield return finding;
            }

            yield break;
        }

        await foreach (var finding in ScanLargestFilesAsync(parameters, progress, cancellationToken).ConfigureAwait(false))
        {
            yield return finding;
        }
    }

    private async IAsyncEnumerable<ScanFinding> ScanLargestFilesAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IProgress<ScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var roots = _rootPath.Resolve(parameters);
        var topN = _topN.Resolve(parameters);
        var minBytes = _minSizeMb.Resolve(parameters) * 1024L * 1024L;
        // Min-heap of the current top-N largest files (priority = size).
        var heap = new PriorityQueue<ScanFinding, long>();
        var completed = 0;
        long discoveredBytes = 0;

        progress?.Report(new ScanProgress(ModuleId, "Starting analysis…", 0));
        // Leave the UI / caller sync context before the first directory walk.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        foreach (var root in roots)
        {
            if (!_fileSystem.DirectoryExists(root))
            {
                continue;
            }

            var canonicalRoot = PathCanonicalizer.Canonicalize(root);
            progress?.Report(new ScanProgress(ModuleId, $"Analyzing {canonicalRoot}", completed, DiscoveredBytes: discoveredBytes));

            foreach (var file in _fileSystem.EnumerateFiles(canonicalRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                completed++;

                FileEntryInfo? info;
                try
                {
                    info = _fileSystem.GetFileInfo(file);
                }
                catch
                {
                    info = null;
                }

                if (info is not null && !info.IsReparsePoint && info.Length >= minBytes)
                {
                    discoveredBytes += info.Length;
                    var finding = new ScanFinding(
                        Id: $"{ModuleId}:{Guid.NewGuid():N}",
                        ModuleId: ModuleId,
                        TargetId: "large-file",
                        DisplayName: Path.GetFileName(info.FullPath),
                        Path: info.FullPath,
                        SizeBytes: info.Length,
                        Risk: FindingRisk.Informational,
                        Details: info.FullPath,
                        IsActionable: false,
                        RequiredPrivilege: RequiredPrivilege.None,
                        AllowedRoot: canonicalRoot);

                    if (heap.Count < topN)
                    {
                        heap.Enqueue(finding, finding.SizeBytes);
                    }
                    else if (heap.TryPeek(out _, out var smallest) && finding.SizeBytes > smallest)
                    {
                        heap.EnqueueDequeue(finding, finding.SizeBytes);
                    }
                }

                if (completed % ProgressEveryFiles == 0)
                {
                    progress?.Report(new ScanProgress(
                        ModuleId,
                        $"Analyzing {canonicalRoot} — {completed:N0} files",
                        completed,
                        DiscoveredBytes: discoveredBytes));
                    await Task.Yield();
                }
            }
        }

        progress?.Report(new ScanProgress(
            ModuleId,
            $"Finishing — {completed:N0} files scanned",
            completed,
            DiscoveredBytes: discoveredBytes));

        var ordered = new List<ScanFinding>(heap.Count);
        while (heap.TryDequeue(out var finding, out _))
        {
            ordered.Add(finding);
        }

        foreach (var finding in ordered.OrderByDescending(f => f.SizeBytes))
        {
            yield return finding;
        }
    }

    private async IAsyncEnumerable<ScanFinding> ScanDuplicatesAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IProgress<ScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var roots = _rootPath.Resolve(parameters);
        var topN = _topN.Resolve(parameters);
        var minBytes = _minSizeMb.Resolve(parameters) * 1024L * 1024L;
        var bySize = new Dictionary<long, List<(string Path, string CanonicalRoot)>>();
        var completed = 0;
        long discoveredBytes = 0;

        progress?.Report(new ScanProgress(ModuleId, "Starting duplicate analysis…", 0));
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        foreach (var root in roots)
        {
            if (!_fileSystem.DirectoryExists(root))
            {
                continue;
            }

            var canonicalRoot = PathCanonicalizer.Canonicalize(root);
            progress?.Report(new ScanProgress(ModuleId, $"Scanning {canonicalRoot}", completed, DiscoveredBytes: discoveredBytes));

            foreach (var file in _fileSystem.EnumerateFiles(canonicalRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                completed++;

                FileEntryInfo? info;
                try
                {
                    info = _fileSystem.GetFileInfo(file);
                }
                catch
                {
                    info = null;
                }

                if (info is not null && !info.IsReparsePoint && info.Length >= minBytes)
                {
                    discoveredBytes += info.Length;
                    if (!bySize.TryGetValue(info.Length, out var list))
                    {
                        list = [];
                        bySize[info.Length] = list;
                    }

                    list.Add((info.FullPath, canonicalRoot));
                }

                if (completed % ProgressEveryFiles == 0)
                {
                    progress?.Report(new ScanProgress(
                        ModuleId,
                        $"Scanning {canonicalRoot} — {completed:N0} files",
                        completed,
                        DiscoveredBytes: discoveredBytes));
                    await Task.Yield();
                }
            }
        }

        var sizeCandidates = bySize
            .Where(kv => kv.Value.Count >= 2)
            .OrderByDescending(kv => kv.Key)
            .ToList();

        progress?.Report(new ScanProgress(
            ModuleId,
            $"Hashing {sizeCandidates.Sum(g => g.Value.Count):N0} size-collision candidates…",
            completed,
            DiscoveredBytes: discoveredBytes));

        var duplicateGroups = new List<(long Size, string Sha1, List<(string Path, string CanonicalRoot)> Members)>();
        var hashed = 0;

        foreach (var (size, members) in sizeCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var byHash = new Dictionary<string, List<(string Path, string CanonicalRoot)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var member in members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hashed++;
                var sha1 = TryComputeSha1(member.Path);
                if (sha1 is null)
                {
                    continue;
                }

                if (!byHash.TryGetValue(sha1, out var hashMembers))
                {
                    hashMembers = [];
                    byHash[sha1] = hashMembers;
                }

                hashMembers.Add(member);

                if (hashed % ProgressEveryFiles == 0)
                {
                    progress?.Report(new ScanProgress(
                        ModuleId,
                        $"Hashing candidates — {hashed:N0}",
                        completed,
                        DiscoveredBytes: discoveredBytes));
                    await Task.Yield();
                }
            }

            foreach (var (sha1, hashMembers) in byHash.Where(kv => kv.Value.Count >= 2))
            {
                duplicateGroups.Add((size, sha1, hashMembers));
            }
        }

        progress?.Report(new ScanProgress(
            ModuleId,
            $"Finishing — {duplicateGroups.Count:N0} duplicate groups",
            completed,
            DiscoveredBytes: discoveredBytes));

        foreach (var group in duplicateGroups.OrderByDescending(g => g.Size).Take(topN))
        {
            var groupId = $"{group.Size}:{group.Sha1}";
            var count = group.Members.Count;
            foreach (var member in group.Members.OrderBy(m => m.Path, StringComparer.OrdinalIgnoreCase))
            {
                yield return new ScanFinding(
                    Id: $"{ModuleId}:{Guid.NewGuid():N}",
                    ModuleId: ModuleId,
                    TargetId: "duplicate-file",
                    DisplayName: Path.GetFileName(member.Path),
                    Path: member.Path,
                    SizeBytes: group.Size,
                    Risk: FindingRisk.Informational,
                    Details: $"{member.Path} — duplicate of {count} files, SHA-1 {group.Sha1[..12]}…",
                    IsActionable: false,
                    RequiredPrivilege: RequiredPrivilege.None,
                    AllowedRoot: member.CanonicalRoot,
                    Metadata: new Dictionary<string, string>
                    {
                        ["sha1"] = group.Sha1,
                        ["duplicateGroupId"] = groupId
                    });
            }
        }
    }

    private string? TryComputeSha1(string path)
    {
        try
        {
            using var stream = _fileSystem.OpenRead(path);
            var hash = SHA1.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }
}
