namespace SlopClean.Core.Abstractions;

public interface IHvciCompatibilityInspector
{
    HvciImageAnalysis AnalyzeDriverImage(string imagePath);
}

public sealed record HvciImageAnalysis(
    bool Analyzed,
    bool IsIncompatibleWithMemoryIntegrity,
    string? Reason)
{
    public static HvciImageAnalysis Unavailable(string reason)
        => new(false, false, reason);

    public static HvciImageAnalysis Compatible()
        => new(true, false, null);

    public static HvciImageAnalysis Incompatible(string reason)
        => new(true, true, reason);
}
