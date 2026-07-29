namespace SlopClean.Core.Parameters;

public sealed class BoolParameter : IModuleParameter
{
    public BoolParameter(string id, string displayName, string description, bool defaultValue)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        DefaultValue = defaultValue;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public object? DefaultValue { get; }
    public Type ValueType => typeof(bool);

    public ParameterValidationResult Validate(object? value)
    {
        if (value is null)
        {
            return ParameterValidationResult.Success();
        }

        return value is bool
            ? ParameterValidationResult.Success()
            : ParameterValidationResult.Fail($"Parameter '{Id}' must be a boolean.");
    }

    public bool Resolve(IReadOnlyDictionary<string, object?> values)
        => values.TryGetValue(Id, out var raw) && raw is bool b ? b : (bool)DefaultValue!;
}
