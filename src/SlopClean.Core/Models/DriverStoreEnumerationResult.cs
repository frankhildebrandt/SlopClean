namespace SlopClean.Core.Models;

public sealed record DriverStoreEnumerationResult(
    bool IsAuthoritative,
    string? FailureReason,
    IReadOnlyList<OemDriverPackage> Packages)
{
    public static DriverStoreEnumerationResult Failed(string reason)
        => new(false, reason, []);

    public static DriverStoreEnumerationResult Succeeded(IReadOnlyList<OemDriverPackage> packages)
        => new(true, null, packages);
}
