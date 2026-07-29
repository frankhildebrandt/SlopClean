using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;
using SlopClean.Modules;

namespace SlopClean.Modules.Tests;

public class ServiceAdvisorModuleTests
{
    [Fact]
    public async Task Produces_read_only_recommendations()
    {
        var services = new FakeServices();
        var catalog = new ServiceAdvisorCatalog
        {
            Version = 1,
            SupportedWindowsBuilds = new ServiceAdvisorCatalog.BuildRange { Min = 0, Max = 999999 },
            Services =
            [
                new ServiceAdvisorCatalog.ServiceAdvice
                {
                    Name = "DiagTrack",
                    DisplayName = "Telemetry",
                    RecommendedStartType = "Disabled",
                    Reason = "test",
                    Risk = "Low"
                }
            ]
        };

        var module = new ServiceAdvisorModule(services, catalog);
        var findings = new List<ScanFinding>();
        await foreach (var finding in module.ScanAsync(new Dictionary<string, object?>(), null, CancellationToken.None))
        {
            findings.Add(finding);
        }

        Assert.Single(findings);
        Assert.False(findings[0].IsActionable);
        Assert.DoesNotContain(module.GetType().GetInterfaces(), i => i == typeof(Core.Modules.IApplicableModule));
    }

    private sealed class FakeServices : IServiceManager
    {
        public IReadOnlyList<WindowsServiceInfo> GetServices()
            => [new("DiagTrack", "Telemetry", "Automatic", "Running", null)];

        public WindowsServiceInfo? GetService(string serviceName)
            => serviceName == "DiagTrack" ? GetServices()[0] : null;
    }
}
