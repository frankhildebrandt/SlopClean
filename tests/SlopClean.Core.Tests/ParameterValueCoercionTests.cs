using System.Text.Json;
using SlopClean.Core.Parameters;

namespace SlopClean.Core.Tests;

public class ParameterValueCoercionTests
{
    [Fact]
    public void PathList_default_is_not_convertible_via_Convert_ToInt32()
    {
        // Documents the Disk Analyzer open crash: ParameterForm still evaluates
        // IntValue for PathList rows; Convert.ToInt32(DefaultValue) must not be used.
        var parameter = new PathListParameter(
            "RootPath",
            "Roots",
            "desc",
            [@"C:\"]);

        Assert.ThrowsAny<Exception>(() => Convert.ToInt32(parameter.DefaultValue));
    }

    [Fact]
    public void ReadInt_for_path_list_parameter_does_not_throw()
    {
        var parameter = new PathListParameter(
            "RootPath",
            "Roots",
            "desc",
            [@"C:\"]);

        var read = ParameterValueCoercion.ReadInt(parameter, parameter.DefaultValue);

        Assert.Equal(0, read);
    }

    [Fact]
    public void CoercePreset_restores_path_list_from_json_array()
    {
        var parameter = new PathListParameter("RootPath", "Roots", "desc", [@"C:\"]);
        using var doc = JsonDocument.Parse("""["D:\\Data","E:\\"]""");

        var coerced = ParameterValueCoercion.CoercePreset(parameter, doc.RootElement);

        var paths = Assert.IsAssignableFrom<IReadOnlyList<string>>(coerced);
        Assert.Equal([@"D:\Data", @"E:\"], paths);
    }

    [Fact]
    public void Format_and_parse_path_list_text_round_trip()
    {
        var text = ParameterValueCoercion.FormatPathList([@"C:\", @"D:\Games"]);
        var paths = ParameterValueCoercion.ParsePathList(text);

        Assert.Equal([@"C:\", @"D:\Games"], paths);
    }
}
