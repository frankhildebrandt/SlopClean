using SlopClean.Core.Logging;

namespace SlopClean.Core.Tests;

public class LogRedactorTests
{
    [Fact]
    public void Redacts_windows_paths()
    {
        var input = @"Failed deleting C:\Users\alice\AppData\Local\Temp\file.tmp";
        var output = LogRedactor.Redact(input);
        Assert.DoesNotContain("alice", output);
        Assert.Contains("[path]", output);
    }

    [Fact]
    public void Enricher_redacts_browser_profile_fragments()
    {
        var input = @"Chrome\Default\Cache locked";
        var output = RedactingEnricher.EnrichMessage(input);
        Assert.Contains("[browser-profile]", output);
        Assert.DoesNotContain("Default\\Cache", output);
    }
}
