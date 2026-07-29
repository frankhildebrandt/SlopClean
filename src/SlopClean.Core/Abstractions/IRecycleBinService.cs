namespace SlopClean.Core.Abstractions;

public interface IRecycleBinService
{
    RecycleBinInfo Query();
    void Empty();
}

public sealed record RecycleBinInfo(long ItemCount, long SizeBytes);
