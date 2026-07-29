namespace SlopClean.Core.Models;

public sealed record ScanProgress(
    string ModuleId,
    string Message,
    int CompletedItems,
    int? TotalItems = null,
    long DiscoveredBytes = 0);
