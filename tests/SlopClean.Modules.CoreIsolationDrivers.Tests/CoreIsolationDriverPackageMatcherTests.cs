using SlopClean.Core.Models;
using SlopClean.Modules.CoreIsolationDrivers;

namespace SlopClean.Modules.CoreIsolationDrivers.Tests;

public class CoreIsolationDriverPackageMatcherTests
{
    private static readonly Guid MediaClass = new("4d36e96c-e325-11ce-bfc1-08002be10318");

    [Fact]
    public void Matches_unique_package_by_referenced_sys_image()
    {
        var package = new OemDriverPackage(
            "oem55.inf",
            "lgjoyhid.inf",
            "Logitech",
            MediaClass,
            "fp-55",
            [],
            0,
            0,
            false,
            ReferencedImageFileNames: ["LGBusEnum.sys", "LGJoyXICore.sys"]);

        var signal = new CodeIntegritySignal(
            3089,
            DateTimeOffset.UtcNow,
            "LGBusEnum.sys",
            null,
            @"C:\Windows\System32\drivers\LGBusEnum.sys is incompatible with HVCI");

        var owners = CoreIsolationDriverPackageMatcher.FindExactPackageOwners([package], signal);

        Assert.Same(package, Assert.Single(owners));
    }

    [Fact]
    public void Does_not_match_when_sys_image_appears_in_multiple_packages()
    {
        var a = new OemDriverPackage(
            "oem1.inf", "a.inf", "A", MediaClass, "fp-1", [], 0, 0, false,
            ReferencedImageFileNames: ["shared.sys"]);
        var b = new OemDriverPackage(
            "oem2.inf", "b.inf", "B", MediaClass, "fp-2", [], 0, 0, false,
            ReferencedImageFileNames: ["shared.sys"]);
        var signal = new CodeIntegritySignal(3089, DateTimeOffset.UtcNow, "shared.sys", null, "shared.sys blocked");

        Assert.Equal(2, CoreIsolationDriverPackageMatcher.FindExactPackageOwners([a, b], signal).Count);
    }
}
