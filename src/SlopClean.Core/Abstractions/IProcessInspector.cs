namespace SlopClean.Core.Abstractions;

public interface IProcessInspector
{
    bool IsProcessRunning(string processName);
    IReadOnlyList<string> GetRunningProcessNames();
}
