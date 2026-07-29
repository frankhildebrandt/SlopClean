using SlopClean.Platform.Windows;

namespace SlopClean.Platform.Windows.Tests;

public class InfOemParserTests
{
    [Fact]
    public void Parses_provider_classguid_and_fingerprint()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SlopCleanInfTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "oem99.inf");
        File.WriteAllText(path,
            """
            [Version]
            Signature="$WINDOWS NT$"
            Class=Media
            ClassGUID={4d36e96c-e325-11ce-bfc1-08002be10318}
            Provider=%Provider%
            DriverVer=01/01/2024,1.0.0.0

            [SourceDisksFiles]
            contoso.sys=1

            [Strings]
            Provider="Contoso Audio"
            """);

        try
        {
            Assert.True(InfOemParser.TryRead(path, out var parsed));
            Assert.Equal("oem99.inf", parsed.PublishedName);
            Assert.Equal("Contoso Audio", parsed.Provider);
            Assert.Equal(Guid.Parse("4d36e96c-e325-11ce-bfc1-08002be10318"), parsed.ClassGuid);
            Assert.Equal("Media", parsed.ClassName);
            Assert.Equal("1.0.0.0", parsed.DriverVersion);
            Assert.Equal(new DateOnly(2024, 1, 1), parsed.DriverDate);
            Assert.Contains("oem99.inf", parsed.PackageFingerprint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("contoso.sys", parsed.ReferencedImageFileNames, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
