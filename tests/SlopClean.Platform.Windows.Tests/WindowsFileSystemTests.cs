using SlopClean.Platform.Windows;

namespace SlopClean.Platform.Windows.Tests;

public class WindowsFileSystemTests
{
    [Fact]
    public void Roundtrips_temp_file_operations_in_sandbox()
    {
        var fs = new WindowsFileSystem();
        var root = Path.Combine(Path.GetTempPath(), "SlopCleanTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "sample.txt");
            File.WriteAllText(file, "hello");
            Assert.True(fs.FileExists(file));
            Assert.False(fs.IsReparsePoint(file));
            fs.DeleteFile(file);
            Assert.False(fs.FileExists(file));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
