using System.Text.RegularExpressions;

namespace SlopClean.Core.Logging;

public static partial class LogRedactor
{
    public static string Redact(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        var redacted = WindowsPathRegex().Replace(message, "[path]");
        redacted = UserProfileRegex().Replace(redacted, "%USERPROFILE%");
        redacted = BrowserProfileRegex().Replace(redacted, "[browser-profile]");
        return redacted;
    }

    [GeneratedRegex(@"[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]*", RegexOptions.Compiled)]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"C:\\Users\\[^\\]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UserProfileRegex();

    [GeneratedRegex(@"(Chrome|Edge|Firefox|Mozilla)\\[^\\]+\\", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BrowserProfileRegex();
}
