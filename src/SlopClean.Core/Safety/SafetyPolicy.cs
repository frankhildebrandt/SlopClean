using SlopClean.Core.Abstractions;
using SlopClean.Core.Models;

namespace SlopClean.Core.Safety;

public sealed class SafetyPolicy
{
    private readonly IFileSystem _fileSystem;
    private readonly HashSet<string> _exactProtectedRoots;
    private readonly string? _windowsRoot;
    private readonly string? _windowsTempRoot;
    private readonly string? _system32Root;
    private readonly string? _sysWow64Root;

    public SafetyPolicy(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
        _exactProtectedRoots = BuildExactProtectedRoots(fileSystem);
        _windowsRoot = TryGet(fileSystem, SpecialFolderKind.Windows);
        _windowsTempRoot = TryGet(fileSystem, SpecialFolderKind.WindowsTemp);
        _system32Root = TryGet(fileSystem, SpecialFolderKind.System);
        _sysWow64Root = _windowsRoot is null
            ? null
            : PathCanonicalizer.Canonicalize(Path.Combine(_windowsRoot, "SysWOW64"));
    }

    public SafetyValidationResult ValidateAction(OptimizationAction action)
    {
        if (!PrivilegedOperationCodes.All.Contains(action.OperationCode))
        {
            return SafetyValidationResult.Deny($"Unknown operation '{action.OperationCode}'.");
        }

        if (action.OperationCode is PrivilegedOperationCodes.EmptyRecycleBin)
        {
            return SafetyValidationResult.Allow();
        }

        if (string.IsNullOrWhiteSpace(action.Path))
        {
            if (action.OperationCode is PrivilegedOperationCodes.DeleteRegistryValue
                or PrivilegedOperationCodes.DeleteRegistryKey)
            {
                return ValidateRegistryAction(action);
            }

            return SafetyValidationResult.Deny("Action path is required.");
        }

        string canonicalPath;
        try
        {
            canonicalPath = PathCanonicalizer.Canonicalize(action.Path);
        }
        catch (Exception ex)
        {
            return SafetyValidationResult.Deny($"Invalid path: {ex.Message}");
        }

        if (IsForbiddenSystemPath(canonicalPath))
        {
            return SafetyValidationResult.Deny("Refusing to modify a protected system or profile path.");
        }

        if (string.IsNullOrWhiteSpace(action.AllowedRoot))
        {
            return SafetyValidationResult.Deny("Action is missing an allowed root.");
        }

        string canonicalRoot;
        try
        {
            canonicalRoot = PathCanonicalizer.Canonicalize(action.AllowedRoot);
        }
        catch (Exception ex)
        {
            return SafetyValidationResult.Deny($"Invalid allowed root: {ex.Message}");
        }

        if (!PathCanonicalizer.IsUnderRoot(canonicalPath, canonicalRoot))
        {
            return SafetyValidationResult.Deny("Path is outside the allowed root.");
        }

        if (_fileSystem.IsReparsePoint(canonicalPath))
        {
            return SafetyValidationResult.Deny("Refusing to follow or modify reparse points.");
        }

        return SafetyValidationResult.Allow();
    }

    public SafetyValidationResult ValidateDeletePath(string path, string allowedRoot)
    {
        return ValidateAction(new OptimizationAction(
            Id: "probe",
            ModuleId: "safety",
            FindingId: "probe",
            OperationCode: PrivilegedOperationCodes.DeleteFile,
            Path: path,
            AllowedRoot: allowedRoot,
            RequiredPrivilege: RequiredPrivilege.None));
    }

    private bool IsForbiddenSystemPath(string canonicalPath)
    {
        var driveRoot = PathCanonicalizer.GetDriveRoot(canonicalPath);
        if (driveRoot is not null
            && string.Equals(driveRoot, canonicalPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_exactProtectedRoots.Contains(canonicalPath))
        {
            return true;
        }

        if (_system32Root is not null
            && (string.Equals(canonicalPath, _system32Root, StringComparison.OrdinalIgnoreCase)
                || PathCanonicalizer.IsUnderRoot(canonicalPath, _system32Root)))
        {
            return true;
        }

        if (_sysWow64Root is not null
            && (string.Equals(canonicalPath, _sysWow64Root, StringComparison.OrdinalIgnoreCase)
                || PathCanonicalizer.IsUnderRoot(canonicalPath, _sysWow64Root)))
        {
            return true;
        }

        if (_windowsRoot is not null
            && (string.Equals(canonicalPath, _windowsRoot, StringComparison.OrdinalIgnoreCase)
                || PathCanonicalizer.IsUnderRoot(canonicalPath, _windowsRoot)))
        {
            // Windows\Temp is the only deletable subtree under Windows.
            if (_windowsTempRoot is not null
                && (string.Equals(canonicalPath, _windowsTempRoot, StringComparison.OrdinalIgnoreCase)
                    || PathCanonicalizer.IsUnderRoot(canonicalPath, _windowsTempRoot)))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    private static SafetyValidationResult ValidateRegistryAction(OptimizationAction action)
    {
        if (action.Payload is null
            || !action.Payload.TryGetValue("hive", out var hive)
            || !action.Payload.TryGetValue("subKey", out var subKey)
            || string.IsNullOrWhiteSpace(hive)
            || string.IsNullOrWhiteSpace(subKey))
        {
            return SafetyValidationResult.Deny("Registry action payload is incomplete.");
        }

        if (subKey.Contains("..", StringComparison.Ordinal))
        {
            return SafetyValidationResult.Deny("Registry path must not contain '..'.");
        }

        var allowedPrefixes = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
        };

        if (!allowedPrefixes.Any(prefix =>
                subKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return SafetyValidationResult.Deny("Registry key is outside the allowed cleanup locations.");
        }

        return SafetyValidationResult.Allow();
    }

    private static HashSet<string> BuildExactProtectedRoots(IFileSystem fileSystem)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(SpecialFolderKind kind)
        {
            var value = TryGet(fileSystem, kind);
            if (value is not null)
            {
                roots.Add(value);
            }
        }

        Add(SpecialFolderKind.Windows);
        Add(SpecialFolderKind.System);
        Add(SpecialFolderKind.UserProfile);
        Add(SpecialFolderKind.ApplicationData);
        Add(SpecialFolderKind.LocalApplicationData);
        Add(SpecialFolderKind.CommonApplicationData);
        return roots;
    }

    private static string? TryGet(IFileSystem fileSystem, SpecialFolderKind kind)
    {
        try
        {
            return PathCanonicalizer.Canonicalize(fileSystem.GetFolderPath(kind));
        }
        catch
        {
            return null;
        }
    }
}
