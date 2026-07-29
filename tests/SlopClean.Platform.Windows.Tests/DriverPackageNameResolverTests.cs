using SlopClean.Platform.Windows;

namespace SlopClean.Platform.Windows.Tests;

public class DriverPackageNameResolverTests
{
    [Fact]
    public void Prefers_driver_node_inf_path_when_it_is_oem_published_name()
    {
        var published = DriverPackageNameResolver.ResolvePublishedOemName(
            driverNodeInfPath: "oem36.inf",
            deviceDriverInfPath: @"C:\Windows\System32\DriverStore\FileRepository\contoso.inf_amd64_1\contoso.inf",
            originalNameToPublished: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["contoso.inf"] = "oem36.inf"
            });

        Assert.Equal("oem36.inf", published);
    }

    [Fact]
    public void Maps_file_repository_inf_to_published_name_via_original_name()
    {
        var published = DriverPackageNameResolver.ResolvePublishedOemName(
            driverNodeInfPath: null,
            deviceDriverInfPath: @"C:\Windows\System32\DriverStore\FileRepository\contoso.inf_amd64_1\contoso.inf",
            originalNameToPublished: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["contoso.inf"] = "oem36.inf"
            });

        Assert.Equal("oem36.inf", published);
    }

    [Fact]
    public void Returns_null_when_no_oem_mapping_exists()
    {
        var published = DriverPackageNameResolver.ResolvePublishedOemName(
            driverNodeInfPath: "netadapterxhci.inf",
            deviceDriverInfPath: @"C:\Windows\INF\netadapterxhci.inf",
            originalNameToPublished: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        Assert.Null(published);
    }
}
