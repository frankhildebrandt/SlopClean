namespace SlopClean.Core.Settings;

public sealed class AppSettings
{
    public string BackupDirectory { get; set; } = DefaultBackupDirectory;

    public static string DefaultBackupDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SlopClean",
            "backups");
}
