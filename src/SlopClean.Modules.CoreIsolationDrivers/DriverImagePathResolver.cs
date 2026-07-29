namespace SlopClean.Modules.CoreIsolationDrivers;

internal static class DriverImagePathResolver
{
    public static IEnumerable<string> CandidatePaths(string imageFileName)
    {
        if (string.IsNullOrWhiteSpace(imageFileName))
        {
            yield break;
        }

        var name = Path.GetFileName(imageFileName.Trim());
        if (string.IsNullOrWhiteSpace(name))
        {
            yield break;
        }

        yield return Path.Combine(Environment.SystemDirectory, "drivers", name);

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
        {
            yield return Path.Combine(windows, "System32", "drivers", name);
            yield return Path.Combine(windows, "SysWOW64", "drivers", name);
        }
    }
}
