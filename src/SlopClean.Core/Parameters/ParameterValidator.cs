namespace SlopClean.Core.Parameters;

public static class ParameterValidator
{
    public static void ValidateAll(
        IEnumerable<IModuleParameter> parameters,
        IReadOnlyDictionary<string, object?> values)
    {
        foreach (var parameter in parameters)
        {
            values.TryGetValue(parameter.Id, out var value);
            var result = parameter.Validate(value);
            if (!result.IsValid)
            {
                throw new ArgumentException(result.ErrorMessage, parameter.Id);
            }
        }
    }

    public static IReadOnlyDictionary<string, object?> WithDefaults(
        IEnumerable<IModuleParameter> parameters,
        IReadOnlyDictionary<string, object?>? values)
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in parameters)
        {
            if (values is not null && values.TryGetValue(parameter.Id, out var provided) && provided is not null)
            {
                map[parameter.Id] = provided;
            }
            else
            {
                map[parameter.Id] = parameter.DefaultValue;
            }
        }

        ValidateAll(parameters, map);
        return map;
    }
}
