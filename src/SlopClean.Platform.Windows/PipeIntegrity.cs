using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SlopClean.Platform.Windows;

/// <summary>
/// Lowers a kernel object's mandatory integrity label so a medium-IL UI process can talk to a
/// high-IL elevated helper over a named pipe owned by the helper.
/// </summary>
public static class PipeIntegrity
{
    private const uint LabelSecurityInformation = 0x00000010;
    private const uint SeKernelObject = 6;
    private const uint SddlRevision1 = 1;

    public static void SetLowMandatoryLabel(SafePipeHandle handle)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
                "S:(ML;;NW;;;LW)",
                SddlRevision1,
                out var sd,
                IntPtr.Zero))
        {
            throw new InvalidOperationException(
                $"Failed to build low-integrity descriptor (Win32 {Marshal.GetLastPInvokeError()}).");
        }

        try
        {
            if (!GetSecurityDescriptorSacl(sd, out var present, out var sacl, out _))
            {
                throw new InvalidOperationException(
                    $"GetSecurityDescriptorSacl failed (Win32 {Marshal.GetLastPInvokeError()}).");
            }

            if (!present || sacl == IntPtr.Zero)
            {
                throw new InvalidOperationException("Low-integrity SACL was not present.");
            }

            var result = SetSecurityInfo(
                handle.DangerousGetHandle(),
                SeKernelObject,
                LabelSecurityInformation,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                sacl);
            if (result != 0)
            {
                throw new InvalidOperationException($"SetSecurityInfo failed (Win32 {result}).");
            }
        }
        finally
        {
            LocalFree(sd);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string stringSecurityDescriptor,
        uint stringSdRevision,
        out IntPtr securityDescriptor,
        IntPtr securityDescriptorSize);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetSecurityDescriptorSacl(
        IntPtr pSecurityDescriptor,
        out bool lpbSaclPresent,
        out IntPtr pSacl,
        out bool lpbSaclDefaulted);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint SetSecurityInfo(
        IntPtr handle,
        uint objectType,
        uint securityInfo,
        IntPtr psidOwner,
        IntPtr psidGroup,
        IntPtr pDacl,
        IntPtr pSacl);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
