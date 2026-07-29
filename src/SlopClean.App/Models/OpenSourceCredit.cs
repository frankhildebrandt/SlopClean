namespace SlopClean.App.Models;

public sealed record OpenSourceCredit(
    string Name,
    Uri ProjectUrl,
    string LicenseId,
    string Excerpt);
