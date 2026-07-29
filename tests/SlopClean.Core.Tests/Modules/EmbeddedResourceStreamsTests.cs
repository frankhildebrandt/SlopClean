using SlopClean.Core.Modules;

namespace SlopClean.Core.Tests.Modules;

public class EmbeddedResourceStreamsTests
{
    [Fact]
    public void OpenRequired_returns_stream_for_suffix_match()
    {
        var assembly = typeof(EmbeddedResourceStreamsTests).Assembly;

        using var stream = EmbeddedResourceStreams.OpenRequired(assembly, ".Assets.illustration.png");
        var buffer = new byte[8];
        Assert.Equal(8, stream.Read(buffer, 0, 8));
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], buffer);
    }

    [Fact]
    public void OpenRequired_throws_when_missing()
    {
        var assembly = typeof(EmbeddedResourceStreamsTests).Assembly;
        Assert.Throws<InvalidOperationException>(() =>
            EmbeddedResourceStreams.OpenRequired(assembly, ".Assets.does-not-exist.png"));
    }

    [Fact]
    public void OpenModuleIllustration_uses_convention_path()
    {
        using var stream = EmbeddedResourceStreams.OpenModuleIllustration(typeof(EmbeddedResourceStreamsTests));
        Assert.True(stream.Length > 0);
    }
}
