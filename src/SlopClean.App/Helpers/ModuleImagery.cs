using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SlopClean.Core.Modules;
using Windows.Storage.Streams;

namespace SlopClean.App.Helpers;

/// <summary>
/// Loads module illustrations from <see cref="IModuleIllustration"/> embedded resources.
/// App branding assets remain packaged with the host.
/// </summary>
public static class ModuleImagery
{
    public const string BrandMarkUri = "ms-appx:///Assets/BrandMark.png";
    public const string AppIconUri = "ms-appx:///Assets/AppIcon.ico";

    public static ImageSource BrandMark { get; } = new BitmapImage(new Uri(BrandMarkUri));

    public static ImageSource Load(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (module is not IModuleIllustration illustration)
        {
            return BrandMark;
        }

        using var stream = illustration.OpenIllustration();
        return FromPngStream(stream);
    }

    private static BitmapImage FromPngStream(Stream stream)
    {
        var image = new BitmapImage();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;

        // Copy into a WinRT stream so BitmapImage does not depend on the managed MemoryStream lifetime.
        var randomAccess = new InMemoryRandomAccessStream();
        using (var output = randomAccess.GetOutputStreamAt(0))
        using (var writer = new DataWriter(output))
        {
            writer.WriteBytes(memory.ToArray());
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
            writer.FlushAsync().AsTask().GetAwaiter().GetResult();
        }

        randomAccess.Seek(0);
        image.SetSource(randomAccess);
        return image;
    }
}
