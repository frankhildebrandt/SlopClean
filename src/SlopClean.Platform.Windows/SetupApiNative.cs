using System.Runtime.InteropServices;

namespace SlopClean.Platform.Windows;

internal static class SetupApiNative
{
    public const uint DigcfAllClasses = 0x00000004;
    public const uint DigcfPresent = 0x00000002;
    public const uint SpdrpHardwareId = 0x00000001;
    public const uint SpdrpClassGuid = 0x00000008;
    public const uint SpdrpDriver = 0x00000009;
    public const uint SpdrpMfg = 0x0000000B;
    public const uint SpdrpService = 0x00000004;

    public static readonly DEVPROPKEY DeviceDriverInfPath = new(
        new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6"),
        21);

    public static readonly DEVPROPKEY DeviceIsPresent = new(
        new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"),
        5);

    public static readonly DEVPROPKEY DeviceInstanceId = new(
        new Guid("78c34fc8-104a-4aca-9ea4-524d52906e73"),
        256);

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;

        public DEVPROPKEY(Guid fmtid, uint pid)
        {
            this.fmtid = fmtid;
            this.pid = pid;
        }
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevsW(
        IntPtr classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDevicePropertyW(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        ref DEVPROPKEY propertyKey,
        out uint propertyType,
        byte[]? propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize,
        uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceRegistryPropertyW(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        byte[]? propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);
}
