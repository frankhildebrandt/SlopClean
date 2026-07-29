using SlopClean.Platform.Windows;

namespace SlopClean.Platform.Windows.Tests;

public class PnPUtilEnumDriversParserTests
{
    [Fact]
    public void Parses_english_and_german_published_and_original_names()
    {
        const string output =
            """
            Microsoft PnP Utility

            Published Name:     oem10.inf
            Original Name:      contoso.inf
            Provider Name:      Contoso

            Veröffentlichter Name:     oem11.inf
            Originalname:              dock.inf
            Anbietername:              DockCo
            """;

        var map = PnPUtilEnumDriversParser.ParseOriginalToPublished(output);

        Assert.Equal("oem10.inf", map["contoso.inf"]);
        Assert.Equal("oem11.inf", map["dock.inf"]);
    }
}
