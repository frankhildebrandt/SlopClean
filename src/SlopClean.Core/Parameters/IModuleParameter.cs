namespace SlopClean.Core.Parameters;

public interface IModuleParameter
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    object? DefaultValue { get; }
    Type ValueType { get; }

    ParameterValidationResult Validate(object? value);
}
