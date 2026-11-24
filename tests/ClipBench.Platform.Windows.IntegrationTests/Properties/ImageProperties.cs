using ClipBench.Platform.Windows.Formats;
using ClipBench.Platform.Windows.IntegrationTests.Support;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace ClipBench.Platform.Windows.IntegrationTests.Properties;

public sealed class ImageProperties
{
    // A bitmap header with any size, bit depth, compression, colour count and header size, followed by some data.
    private static readonly Gen<byte[]> _anyHeader =
        Gen.Zip(
            Gen.Zip(Gen.Elements(-1, 0, 1, 2, 3, 255, 65_535, 70_000, int.MaxValue, int.MinValue), Gen.Choose(-70_000, 70_000)),
            Gen.Zip(
                Gen.Zip(Gen.Elements(0, 1, 2, 4, 8, 15, 16, 24, 32, 33, 64), Gen.Choose(0, 10)),
                Gen.Zip(Gen.Elements(0, 1, 256, 257, int.MaxValue, -1), Gen.Elements(12, 40, 52, 56, 64, 108, 124, 200))),
            (size, rest) => (size, depth: rest.Item1, extra: rest.Item2))
        .Select(header => DibBuilder.Join(
                DibBuilder.InfoHeader(header.size.Item1, header.size.Item2, header.depth.Item1, header.depth.Item2, header.extra.Item1, headerSize: header.extra.Item2),
                new byte[Math.Abs(header.size.Item2 % 4096)]));

    private static readonly Gen<byte[]> _goodPngs =
        Gen.Zip(Gen.Zip(Gen.Choose(1, 24), Gen.Choose(1, 24)), Gen.Zip(Gen.Elements(3, 4), Gen.Choose(0, 1000)), (size, kind) =>
            PngBuilder.Build(size.Item1, size.Item2, kind.Item1, PngBuilder.RandomPixels(size.Item1, size.Item2, kind.Item1, kind.Item2)));

    private static bool DibHolds(byte[] dib)
    {
        var png = Dib.ToPng(dib);
        if (png.IsFailure)
        {
            return true;
        }

        var image = Png.Decode(png.Value);
        return image.IsSuccess && (long)image.Value.Width * image.Value.Height <= Png.MaxPixels;
    }

    private static bool PngHolds(byte[] png)
    {
        var image = Png.Decode(png);
        return image.IsFailure
            || ((long)image.Value.Width * image.Value.Height <= Png.MaxPixels
                && image.Value.Pixels.Length == image.Value.Width * image.Value.Height * image.Value.Channels);
    }

    [Property]
    public Property Any_bytes_as_a_bitmap_never_throw() =>
        Prop.ForAll(Arb.From(ByteGenerators.Random(8 * 1024)), DibHolds);

    [Property]
    public Property Any_bitmap_header_never_throws_and_never_passes_the_pixel_cap() =>
        Prop.ForAll(Arb.From(_anyHeader), DibHolds);

    [Property]
    public Property Damaged_bitmaps_never_throw() =>
        Prop.ForAll(Arb.From(ByteGenerators.Damaged(_goodPngs.Select(png => Dib.FromPng(png).Value))), DibHolds);

    [Property]
    public Property Any_bytes_as_a_PNG_never_throw() =>
        Prop.ForAll(Arb.From(ByteGenerators.Random(8 * 1024)), PngHolds);

    [Property]
    public Property Damaged_PNGs_never_throw_and_a_picture_read_has_every_pixel() =>
        Prop.ForAll(Arb.From(ByteGenerators.Damaged(_goodPngs)), PngHolds);
}
