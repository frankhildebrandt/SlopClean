namespace SlopClean.Core.Parameters;

public sealed record ParameterValidationResult(bool IsValid, string? ErrorMessage = null)
{
    public static ParameterValidationResult Success() => new(true);
    public static ParameterValidationResult Fail(string message) => new(false, message);
}
