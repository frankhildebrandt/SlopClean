using System.Runtime.InteropServices;
using SlopClean.Core.Abstractions;

namespace SlopClean.Platform.Windows;

public sealed class WindowsRecycleBinService : IRecycleBinService
{
    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    public RecycleBinInfo Query()
    {
        long itemCount = 0;
        long sizeBytes = 0;

        // Approximate via $Recycle.Bin under each fixed drive; Shell COM query varies by OS SKU.
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            var recycle = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
            if (!Directory.Exists(recycle))
            {
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(recycle, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        itemCount++;
                        sizeBytes += info.Length;
                    }
                    catch
                    {
                        // ignore inaccessible entries
                    }
                }
            }
            catch
            {
                // ignore inaccessible bins
            }
        }

        return new RecycleBinInfo(itemCount, sizeBytes);
    }

    public void Empty()
    {
        var result = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        // 0 = S_OK; negative HRESULTs indicate failure. Empty-bin edge cases are ignored.
        if (result < 0 && result != unchecked((int)0x8000FFFF))
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
}
