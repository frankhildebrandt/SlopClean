namespace SlopClean.Core.Safety;

/// <summary>
/// Well-known device setup class GUIDs that must never be deleted by SlopClean.
/// </summary>
public static class CriticalDriverClassGuids
{
    public static readonly Guid Computer = new("4d36e966-e325-11ce-bfc1-08002be10318");
    public static readonly Guid DiskDrive = new("4d36e967-e325-11ce-bfc1-08002be10318");
    public static readonly Guid Hdc = new("4d36e96a-e325-11ce-bfc1-08002be10318");
    public static readonly Guid ScsiAdapter = new("4d36e97b-e325-11ce-bfc1-08002be10318");
    public static readonly Guid System = new("4d36e97d-e325-11ce-bfc1-08002be10318");
    public static readonly Guid Volume = new("71a27cdd-812a-11d0-bec7-08002be2092f");
    public static readonly Guid VolumeSnapshot = new("533c5b84-ec70-11d2-9505-00c04f79deaf");
    public static readonly Guid Processor = new("50127dc3-0f36-415e-a6cc-4cb3be910b65");

    private static readonly HashSet<Guid> Denied =
    [
        Computer,
        DiskDrive,
        Hdc,
        ScsiAdapter,
        System,
        Volume,
        VolumeSnapshot,
        Processor
    ];

    public static bool IsDenied(Guid classGuid) => Denied.Contains(classGuid);

    public static bool TryParse(string? text, out Guid classGuid)
        => Guid.TryParse(text, out classGuid);
}
