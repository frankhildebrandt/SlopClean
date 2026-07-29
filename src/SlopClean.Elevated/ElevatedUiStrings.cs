using System.Globalization;

namespace SlopClean.Elevated;

internal static class ElevatedUiStrings
{
    private static bool IsGerman
        => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("de", StringComparison.OrdinalIgnoreCase);

    public static string WindowTitle => IsGerman ? "SlopClean (Administrator)" : "SlopClean (Administrator)";

    public static string Heading => IsGerman ? "Administrator-Hilfe" : "Administrator helper";

    public static string StatusLabel => IsGerman ? "Status" : "Status";

    public static string JobLabel => IsGerman ? "Aktueller Auftrag" : "Current job";

    public static string StatusWaiting => IsGerman ? "Verbindung wird hergestellt…" : "Connecting…";

    public static string StatusReady => IsGerman ? "Bereit — warte auf Aufträge…" : "Ready — waiting for jobs…";

    public static string StatusWorking => IsGerman ? "Wird ausgeführt…" : "Working…";

    public static string StatusFinished => IsGerman ? "Fertig" : "Finished";

    public static string StatusFailed => IsGerman ? "Fehlgeschlagen" : "Failed";

    public static string JobNone => IsGerman ? "—" : "—";
}
