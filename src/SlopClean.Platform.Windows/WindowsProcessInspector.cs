using System.Diagnostics;
using SlopClean.Core.Abstractions;

namespace SlopClean.Platform.Windows;

public sealed class WindowsProcessInspector : IProcessInspector
{
    public bool IsProcessRunning(string processName)
    {
        var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(processName)
            : processName;
        return Process.GetProcessesByName(name).Length > 0;
    }

    public IReadOnlyList<string> GetRunningProcessNames()
        => Process.GetProcesses()
            .Select(p =>
            {
                try { return p.ProcessName; }
                catch { return null; }
            })
            .Where(n => n is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
