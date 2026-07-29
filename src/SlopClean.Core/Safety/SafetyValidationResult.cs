namespace SlopClean.Core.Safety;

public sealed record SafetyValidationResult(bool IsAllowed, string? Reason = null)
{
    public static SafetyValidationResult Allow() => new(true);
    public static SafetyValidationResult Deny(string reason) => new(false, reason);
}
