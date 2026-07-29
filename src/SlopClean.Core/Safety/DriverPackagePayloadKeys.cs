namespace SlopClean.Core.Safety;

public static class DriverPackagePayloadKeys
{
    public const string PublishedName = "publishedName";
    public const string ClassGuid = "classGuid";
    public const string PackageFingerprint = "packageFingerprint";
    public const string RemovalMode = "removalMode";
    public const string AllowInUse = "allowInUse";
    public const string Provider = "provider";
    public const string OriginalName = "originalName";
    public const string IsBootCritical = "isBootCritical";
    public const string IsMicrosoftProvider = "isMicrosoftProvider";
    public const string RestorePayloadDirectory = "restorePayloadDirectory";
    public const string BestEffortRestore = "bestEffortRestore";

    public const string RemovalModeOrphan = "orphan";
    public const string RemovalModeInUse = "in-use";
}
