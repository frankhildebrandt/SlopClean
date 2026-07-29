namespace SlopClean.Core.Parameters;

public sealed class EnumParameter : IModuleParameter
{
    private readonly HashSet<string> _allowed;

    public EnumParameter(
        string id,
        string displayName,
        string description,
        string defaultValue,
        IEnumerable<string> allowedValues)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        DefaultValue = defaultValue;
        AllowedValues = allowedValues.ToArray();
        _allowed = new HashSet<string>(AllowedValues, StringComparer.OrdinalIgnoreCase);
        if (!_allowed.Contains(defaultValue))
        {
            throw new ArgumentException("Default value must be one of the allowed values.", nameof(defaultValue));
        }
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public object? DefaultValue { get; }
    public IReadOnlyList<string> AllowedValues { get; }
    public Type ValueType => typeof(string);

    public ParameterValidationResult Validate(object? value)
    {
        if (value is null)
        {
            return ParameterValidationResult.Success();
        }

        if (value is not string s || string.IsNullOrWhiteSpace(s))
        {
            return ParameterValidationResult.Fail($"Parameter '{Id}' must be a non-empty string.");
        }

        return _allowed.Contains(s)
            ? ParameterValidationResult.Success()
            : ParameterValidationResult.Fail($"Parameter '{Id}' value '{s}' is not allowed.");
    }

    public string Resolve(IReadOnlyDictionary<string, object?> values)
        => values.TryGetValue(Id, out var raw) && raw is string s ? s : (string)DefaultValue!;
}
