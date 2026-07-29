namespace SlopClean.Core.Settings;

public sealed class AppSettings
{
    public string BackupDirectory { get; set; } = DefaultBackupDirectory;

    /// <summary>UI language label: System, Deutsch, or English.</summary>
    public string Language { get; set; } = "System";

    public static string DefaultBackupDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlopClean",
            "backups");
}
