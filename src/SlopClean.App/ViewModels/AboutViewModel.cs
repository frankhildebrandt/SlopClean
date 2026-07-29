using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Windows.ApplicationModel.Resources;
using SlopClean.App.Models;

namespace SlopClean.App.ViewModels;

public sealed class AboutViewModel : ObservableObject
{
    private static readonly Uri SourceUri = new("https://github.com/frankhildebrandt/SlopClean");
    private static readonly Uri LicenseUri = new("https://www.gnu.org/licenses/agpl-3.0.html");

    public AboutViewModel()
    {
        var resources = new ResourceLoader();

        Title = resources.GetString("AboutPageTitle");
        Blurb = resources.GetString("AboutBlurb");
        VersionText = string.Format(
            resources.GetString("AboutVersionFormat"),
            ResolveVersion());
        SourceLabel = resources.GetString("AboutSourceLabel");
        LicenseLabel = resources.GetString("AboutLicenseLabel");
        ReviewHint = resources.GetString("AboutReviewHint");
        OpenSourceSectionTitle = resources.GetString("AboutOpenSourceTitle");

        Credits =
        [
            new OpenSourceCredit(
                ".NET",
                new Uri("https://github.com/dotnet/runtime"),
                "MIT",
                resources.GetString("OssDotNetExcerpt")),
            new OpenSourceCredit(
                "Windows App SDK / WinUI 3",
                new Uri("https://github.com/microsoft/WindowsAppSDK"),
                "MIT",
                resources.GetString("OssWindowsAppSdkExcerpt")),
            new OpenSourceCredit(
                "Community Toolkit MVVM",
                new Uri("https://github.com/CommunityToolkit/dotnet"),
                "MIT",
                resources.GetString("OssCommunityToolkitExcerpt")),
            new OpenSourceCredit(
                "Microsoft.Extensions",
                new Uri("https://github.com/dotnet/runtime"),
                "MIT",
                resources.GetString("OssMicrosoftExtensionsExcerpt")),
            new OpenSourceCredit(
                "Serilog",
                new Uri("https://github.com/serilog/serilog"),
                "Apache-2.0",
                resources.GetString("OssSerilogExcerpt")),
        ];
    }

    public string Title { get; }
    public string Blurb { get; }
    public string VersionText { get; }
    public string SourceLabel { get; }
    public Uri SourceUrl => SourceUri;
    public string LicenseLabel { get; }
    public Uri LicenseUrl => LicenseUri;
    public string ReviewHint { get; }
    public string OpenSourceSectionTitle { get; }
    public IReadOnlyList<OpenSourceCredit> Credits { get; }

    private static string ResolveVersion()
    {
        var assembly = typeof(AboutViewModel).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    }
}
