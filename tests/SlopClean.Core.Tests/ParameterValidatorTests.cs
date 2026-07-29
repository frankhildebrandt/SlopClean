using SlopClean.Core.Parameters;

namespace SlopClean.Core.Tests;

public class ParameterValidatorTests
{
    [Fact]
    public void Applies_defaults_and_validates()
    {
        var parameters = new IModuleParameter[]
        {
            new BoolParameter("IncludeUserTemp", "User", "desc", true),
            new IntParameter("MinAgeDays", "Age", "desc", 0, 0, 10)
        };

        var resolved = ParameterValidator.WithDefaults(parameters, new Dictionary<string, object?>
        {
            ["MinAgeDays"] = 3
        });

        Assert.Equal(true, resolved["IncludeUserTemp"]);
        Assert.Equal(3, resolved["MinAgeDays"]);
    }

    [Fact]
    public void Rejects_out_of_range_int()
    {
        var parameters = new IModuleParameter[]
        {
            new IntParameter("MinAgeDays", "Age", "desc", 0, 0, 10)
        };

        Assert.Throws<ArgumentException>(() =>
            ParameterValidator.WithDefaults(parameters, new Dictionary<string, object?>
            {
                ["MinAgeDays"] = 99
            }));
    }
}
