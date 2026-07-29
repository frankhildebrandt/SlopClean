using System.Runtime.CompilerServices;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;
using SlopClean.Core.Safety;

namespace SlopClean.Modules.DiskAnalyzer;

public sealed class DiskAnalyzerModule : IScannableModule
{
    public const string ModuleId = "disk-analyzer";

    private readonly IFileSystem _fileSystem;
    private readonly PathListParameter _rootPath;
    private readonly IntParameter _topN;
    private readonly IntParameter _minSizeMb;

    public DiskAnalyzerModule(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
        var defaultRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        _rootPath = new PathListParameter(
            "RootPath",
            "Roots",
            "Drive or folder roots to analyze.",
            [defaultRoot]);
        _topN = new IntParameter("TopN", "Top N", "Number of largest items to report.", 50, 1, 500);
        _minSizeMb = new IntParameter("MinSizeMb", "Minimum size (MB)", "Ignore files smaller than this.", 50, 1, 1024 * 1024);
    }

    public string Id => ModuleId;
    public string Name => "Disk Analyzer";
    public string Description => "Finds the largest files and folders. Analysis only — no deletion.";
    public ModuleCategory Category => ModuleCategory.Analysis;
    public IReadOnlyList<IModuleParameter> Parameters => [_rootPath, _topN, _minSizeMb];

    public async IAsyncEnumerable<ScanFinding> ScanAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IProgress<ScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var roots = _rootPath.Resolve(parameters);
        var topN = _topN.Resolve(parameters);
        var minBytes = _minSizeMb.Resolve(parameters) * 1024L * 1024L;
        var heap = new List<ScanFinding>();
        var completed = 0;

        foreach (var root in roots)
        {
            if (!_fileSystem.DirectoryExists(root))
            {
                continue;
            }

            var canonicalRoot = PathCanonicalizer.Canonicalize(root);
            foreach (var file in _fileSystem.EnumerateFiles(canonicalRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                completed++;
                if (completed % 250 == 0)
                {
                    progress?.Report(new ScanProgress(ModuleId, $"Analyzing {canonicalRoot}", completed));
                    await Task.Yield();
                }

                var info = _fileSystem.GetFileInfo(file);
                if (info is null || info.IsReparsePoint || info.Length < minBytes)
                {
                    continue;
                }

                heap.Add(new ScanFinding(
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
                    AllowedRoot: canonicalRoot));
            }
        }

        foreach (var finding in heap.OrderByDescending(f => f.SizeBytes).Take(topN))
        {
            yield return finding;
        }
    }
}
