using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Core.Modules;
using SlopClean.Core.Parameters;

namespace SlopClean.Modules;

public sealed class ServiceAdvisorModule : IScannableModule
{
    public const string ModuleId = "service-advisor";

    private readonly IServiceManager _services;
    private readonly ServiceAdvisorCatalog _catalog;

    public ServiceAdvisorModule(IServiceManager services, ServiceAdvisorCatalog? catalog = null)
    {
        _services = services;
        _catalog = catalog ?? ServiceAdvisorCatalog.LoadEmbedded();
    }

    public string Id => ModuleId;
    public string Name => "Service Advisor";
    public string Description => "Read-only recommendations for optional Windows services. Never changes start types.";
    public ModuleCategory Category => ModuleCategory.Services;
    public IReadOnlyList<IModuleParameter> Parameters => [];

    public async IAsyncEnumerable<ScanFinding> ScanAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IProgress<ScanProgress>? progress,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var build = Environment.OSVersion.Version.Build;
        if (build < _catalog.SupportedWindowsBuilds.Min || build > _catalog.SupportedWindowsBuilds.Max)
        {
            yield return new ScanFinding(
                Id: $"{ModuleId}:unsupported-build",
                ModuleId: ModuleId,
                TargetId: "catalog",
                DisplayName: "Unsupported Windows build",
                Path: null,
                SizeBytes: 0,
                Risk: FindingRisk.Informational,
                Details: $"Catalog supports builds {_catalog.SupportedWindowsBuilds.Min}-{_catalog.SupportedWindowsBuilds.Max}; current is {build}.",
                IsActionable: false,
                RequiredPrivilege: RequiredPrivilege.None,
                AllowedRoot: null);
            yield break;
        }

        var completed = 0;
        foreach (var advice in _catalog.Services)
        {
            cancellationToken.ThrowIfCancellationRequested();
            completed++;
            progress?.Report(new ScanProgress(ModuleId, $"Checking {advice.Name}", completed, _catalog.Services.Count));

            var current = _services.GetService(advice.Name);
            if (current is null)
            {
                continue;
            }

            var matches = string.Equals(current.StartType, advice.RecommendedStartType, StringComparison.OrdinalIgnoreCase);
            yield return new ScanFinding(
                Id: $"{ModuleId}:{advice.Name}",
                ModuleId: ModuleId,
                TargetId: advice.Name,
                DisplayName: current.DisplayName,
                Path: null,
                SizeBytes: 0,
                Risk: advice.Risk.Equals("Medium", StringComparison.OrdinalIgnoreCase) ? FindingRisk.Medium : FindingRisk.Low,
                Details: matches
                    ? $"Already {current.StartType}. {advice.Reason}"
                    : $"Current: {current.StartType}. Recommended: {advice.RecommendedStartType}. {advice.Reason}",
                IsActionable: false,
                RequiredPrivilege: RequiredPrivilege.None,
                AllowedRoot: null,
                Metadata: new Dictionary<string, string>
                {
                    ["serviceName"] = advice.Name,
                    ["currentStartType"] = current.StartType,
                    ["recommendedStartType"] = advice.RecommendedStartType
                });

            await Task.Yield();
        }
    }
}

public sealed class ServiceAdvisorCatalog
{
    public int Version { get; set; }
    public BuildRange SupportedWindowsBuilds { get; set; } = new();
    public List<ServiceAdvice> Services { get; set; } = [];

    public static ServiceAdvisorCatalog LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("service-advisor.json", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("service-advisor.json resource missing.");
        return JsonSerializer.Deserialize<ServiceAdvisorCatalog>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Invalid service advisor catalog.");
    }

    public sealed class BuildRange
    {
        public int Min { get; set; }
        public int Max { get; set; }
    }

    public sealed class ServiceAdvice
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string RecommendedStartType { get; set; } = "";
        public string Reason { get; set; } = "";
        public string Risk { get; set; } = "Low";
    }
}
