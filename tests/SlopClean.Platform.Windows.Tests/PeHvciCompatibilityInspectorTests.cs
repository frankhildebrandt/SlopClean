using System.Text;
using SlopClean.Platform.Windows;

namespace SlopClean.Platform.Windows.Tests;

public class PeHvciCompatibilityInspectorTests
{
    [Fact]
    public void Detects_writable_executable_section()
    {
        var path = WriteTempPe(wxSection: true);
        try
        {
            var result = new PeHvciCompatibilityInspector().AnalyzeDriverImage(path);
            Assert.True(result.Analyzed);
            Assert.True(result.IsIncompatibleWithMemoryIntegrity);
            Assert.Contains("writable+executable", result.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Accepts_pe_without_writable_executable_section()
    {
        var path = WriteTempPe(wxSection: false);
        try
        {
            var result = new PeHvciCompatibilityInspector().AnalyzeDriverImage(path);
            Assert.True(result.Analyzed);
            Assert.False(result.IsIncompatibleWithMemoryIntegrity);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempPe(bool wxSection)
    {
        var dir = Path.Combine(Path.GetTempPath(), "SlopCleanPeHvci", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "sample.sys");
        File.WriteAllBytes(path, BuildMinimalPe(wxSection));
        return path;
    }

    /// <summary>
    /// Minimal PE32+ with one section; characteristics optionally include MEM_EXECUTE|MEM_WRITE.
    /// </summary>
    private static byte[] BuildMinimalPe(bool wxSection)
    {
        const int peOffset = 0x80;
        const ushort numberOfSections = 1;
        const ushort sizeOfOptionalHeader = 0xF0; // PE32+
        var sectionChars = wxSection
            ? 0xE0000020u // CNT_CODE | MEM_EXECUTE | MEM_READ | MEM_WRITE
            : 0x60000020u; // CNT_CODE | MEM_EXECUTE | MEM_READ

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        // DOS header
        bw.Write((ushort)0x5A4D);
        bw.Write(new byte[0x3A]);
        bw.Write(peOffset);

        while (ms.Length < peOffset)
        {
            bw.Write((byte)0);
        }

        // PE signature + COFF
        bw.Write(0x00004550u);
        bw.Write((ushort)0x8664); // Machine AMD64
        bw.Write(numberOfSections);
        bw.Write(0); // TimeDateStamp
        bw.Write(0); // PointerToSymbolTable
        bw.Write(0); // NumberOfSymbols
        bw.Write(sizeOfOptionalHeader);
        bw.Write((ushort)0x022); // Characteristics

        // Optional header PE32+ (240 bytes) — mostly zeros; Magic at start
        var optStart = ms.Position;
        bw.Write((ushort)0x20B);
        bw.Write(new byte[sizeOfOptionalHeader - 2]);
        ms.Position = optStart + sizeOfOptionalHeader;

        // Section header (40 bytes)
        var name = Encoding.ASCII.GetBytes(".text\0\0\0");
        bw.Write(name);
        bw.Write(0x200u); // VirtualSize
        bw.Write(0x1000u); // VirtualAddress
        bw.Write(0x200u); // SizeOfRawData
        bw.Write(0x200u); // PointerToRawData
        bw.Write(0);
        bw.Write(0);
        bw.Write((ushort)0);
        bw.Write((ushort)0);
        bw.Write(sectionChars);

        // Raw section data
        while (ms.Length < 0x400)
        {
            bw.Write((byte)0);
        }

        return ms.ToArray();
    }
}
