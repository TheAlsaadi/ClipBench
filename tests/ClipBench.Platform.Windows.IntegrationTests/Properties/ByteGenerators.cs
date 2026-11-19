using FsCheck;
using FsCheck.Fluent;

namespace ClipBench.Platform.Windows.IntegrationTests.Properties;

/// <summary>Byte inputs for the clipboard format readers: random bytes, and good samples with damage done to them.</summary>
internal static class ByteGenerators
{
    /// <summary>Random bytes, up to the given length. They come from a seed FsCheck picks, so a run is repeatable.</summary>
    public static Gen<byte[]> Random(int maxLength) =>
        Gen.Zip(Gen.Choose(0, maxLength), Gen.Choose(int.MinValue, int.MaxValue), (length, seed) =>
        {
            var bytes = new byte[length];
            new System.Random(seed).NextBytes(bytes);
            return bytes;
        });

    /// <summary>
    /// A good sample with up to eight kinds of damage: bytes overwritten with random or edge values (0, 0x7F, 0x80,
    /// 0xFF), a cut at a random place, or random bytes added at the end.
    /// </summary>
    public static Gen<byte[]> Damaged(Gen<byte[]> samples) =>
        Gen.Zip(samples, Gen.Choose(int.MinValue, int.MaxValue), (sample, seed) =>
        {
            var random = new System.Random(seed);
            var bytes = new List<byte>(sample);
            var changes = random.Next(1, 9);
            for (var change = 0; change < changes && bytes.Count > 0; change++)
            {
                switch (random.Next(4))
                {
                    case 0:
                        bytes[random.Next(bytes.Count)] = (byte)random.Next(256);
                        break;
                    case 1:
                        bytes[random.Next(bytes.Count)] = new byte[] { 0, 0x7F, 0x80, 0xFF }[random.Next(4)];
                        break;
                    case 2:
                        bytes = [.. bytes.Take(random.Next(bytes.Count + 1))];
                        break;
                    default:
                        var tail = new byte[random.Next(64)];
                        random.NextBytes(tail);
                        bytes.AddRange(tail);
                        break;
                }
            }

            return bytes.ToArray();
        });

    /// <summary>Any string, never null, often with markup, entities, line breaks and characters outside ASCII.</summary>
    public static Gen<string> Text { get; } = Gen.Frequency(
        (3, ArbMap.Default.GeneratorFor<string>().Select(text => text ?? string.Empty)),
        (2, Gen.Elements(
                "<", ">", "</", "<b>", "</b>", "<p>", "<!--", "-->", "<script>", "&amp;", "&lt;", "&#", "&#x", ";", "&#1114112;",
                "&nbsp;", "\"", "'", "=", "\r\n", "\0", "é", "😀", "ي", "a", " ")
            .ListOf()
            .Select(string.Concat)));
}
