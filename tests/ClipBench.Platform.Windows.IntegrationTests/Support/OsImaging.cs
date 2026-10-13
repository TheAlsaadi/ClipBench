using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ClipBench.Platform.Windows.IntegrationTests.Support;

/// <summary>
/// Windows' own PNG decoder and encoder. Tests use them as an independent opinion on whether the PNG
/// files ClipBench writes are valid, and as a source of PNG files ClipBench did not write.
/// </summary>
internal static class OsImaging
{
    /// <summary>Decodes a PNG to straight (non-premultiplied) RGBA bytes, top row first.</summary>
    public static async Task<(int Width, int Height, byte[] Rgba)> DecodeAsync(byte[] png)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Straight,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        return ((int)decoder.PixelWidth, (int)decoder.PixelHeight, pixels.DetachPixelData());
    }

    /// <summary>Encodes straight RGBA bytes as a PNG the way Windows does, which picks its own row filters.</summary>
    public static async Task<byte[]> EncodeAsync(int width, int height, byte[] rgba)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight, (uint)width, (uint)height, 96, 96, rgba);
        await encoder.FlushAsync();

        stream.Seek(0);
        var bytes = new byte[stream.Size];
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return bytes;
    }
}
