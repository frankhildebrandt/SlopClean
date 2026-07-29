namespace SlopClean.Core.Parameters;

public sealed class IntParameter : IModuleParameter
{
    public IntParameter(
        string id,
        string displayName,
        string description,
        int defaultValue,
        int? min = null,
        int? max = null)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        DefaultValue = defaultValue;
        Min = min;
        Max = max;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public object? DefaultValue { get; }
    public int? Min { get; }
    public int? Max { get; }
    public Type ValueType => typeof(int);

    public ParameterValidationResult Validate(object? value)
    {
        if (value is null)
        {
            return ParameterValidationResult.Success();
        }

        if (value is not int i)
        {
            return ParameterValidationResult.Fail($"Parameter '{Id}' must be an integer.");
        }

        if (Min is int min && i < min)
        {
            return ParameterValidationResult.Fail($"Parameter '{Id}' must be >= {min}.");
        }

        if (Max is int max && i > max)
        {
            return ParameterValidationResult.Fail($"Parameter '{Id}' must be <= {max}.");
        }

        return ParameterValidationResult.Success();
    }

    public int Resolve(IReadOnlyDictionary<string, object?> values)
        => values.TryGetValue(Id, out var raw) && raw is int i ? i : (int)DefaultValue!;
}
