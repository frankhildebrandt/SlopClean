using SlopClean.Core.Abstractions;

namespace SlopClean.Platform.Windows;

/// <summary>
/// Lightweight local HVCI / Memory Integrity heuristic (not a full hvciscan replacement).
/// Flags PE images with writable+executable sections, a common reason Windows Security
/// lists drivers as incompatible with Memory Integrity.
/// </summary>
public sealed class PeHvciCompatibilityInspector : IHvciCompatibilityInspector
{
    private const uint ImageScnMemExecute = 0x2000_0000;
    private const uint ImageScnMemWrite = 0x8000_0000;

    public HvciImageAnalysis AnalyzeDriverImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return HvciImageAnalysis.Unavailable("Image path is empty.");
        }

        try
        {
            if (!File.Exists(imagePath))
            {
                return HvciImageAnalysis.Unavailable($"Image not found: {Path.GetFileName(imagePath)}");
            }

            using var stream = File.OpenRead(imagePath);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt16() != 0x5A4D)
            {
                return HvciImageAnalysis.Unavailable("Not a PE image (missing MZ).");
            }

            stream.Position = 0x3C;
            var peOffset = reader.ReadInt32();
            if (peOffset <= 0 || peOffset > stream.Length - 24)
            {
                return HvciImageAnalysis.Unavailable("Invalid PE header offset.");
            }

            stream.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550)
            {
                return HvciImageAnalysis.Unavailable("Missing PE signature.");
            }

            stream.Position = peOffset + 6;
            var numberOfSections = reader.ReadUInt16();
            stream.Position = peOffset + 20;
            var sizeOfOptionalHeader = reader.ReadUInt16();
            var sectionTable = peOffset + 24 + sizeOfOptionalHeader;
            if (sectionTable <= 0 || sectionTable + (numberOfSections * 40L) > stream.Length)
            {
                return HvciImageAnalysis.Unavailable("Invalid section table.");
            }

            for (var i = 0; i < numberOfSections; i++)
            {
                stream.Position = sectionTable + (i * 40) + 36;
                var characteristics = reader.ReadUInt32();
                if ((characteristics & ImageScnMemExecute) != 0 && (characteristics & ImageScnMemWrite) != 0)
                {
                    return HvciImageAnalysis.Incompatible(
                        "Driver image has a writable+executable section (incompatible with Memory Integrity / HVCI).");
                }
            }

            return HvciImageAnalysis.Compatible();
        }
        catch (Exception ex)
        {
            return HvciImageAnalysis.Unavailable(ex.Message);
        }
    }
}
