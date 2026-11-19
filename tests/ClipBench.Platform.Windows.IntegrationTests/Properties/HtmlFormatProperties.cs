using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClipBench.Platform.Windows.Formats;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace ClipBench.Platform.Windows.IntegrationTests.Properties;

public sealed partial class HtmlFormatProperties
{
    private static readonly Gen<int> _caps = Gen.Frequency((1, Gen.Constant(0)), (4, Gen.Choose(1, 1 << 20)));

    // A real HTML Format block whose offsets are swapped for numbers that point anywhere: before the start, past the
    // end, into the header, out of order, or too large for an int.
    private static readonly Gen<byte[]> _badOffsets =
        Gen.Zip(ByteGenerators.Text, Gen.Choose(int.MinValue, int.MaxValue), (fragment, seed) =>
        {
            var random = new Random(seed);
            var text = Encoding.Latin1.GetString(HtmlFormat.Build(fragment));
            var replaced = Offset().Replace(text, match => match.Groups["name"].Value + ":" + random.Next(6) switch
            {
                0 => "-1",
                1 => "99999999999999999999",
                2 => random.Next(0, text.Length + 50).ToString("D10", CultureInfo.InvariantCulture),
                3 => string.Empty,
                4 => "0x10",
                _ => match.Groups["value"].Value,
            });
            return Encoding.Latin1.GetBytes(replaced);
        });

    [GeneratedRegex(@"(?<name>StartHTML|EndHTML|StartFragment|EndFragment|StartSelection|EndSelection):(?<value>-?\d+)")]
    private static partial Regex Offset();

    private static bool Holds(byte[] data, int cap)
    {
        var fragment = HtmlFormat.ExtractFragment(data, cap);
        return fragment.IsFailure || Encoding.UTF8.GetByteCount(fragment.Value) <= cap;
    }

    [Property]
    public Property Any_bytes_never_throw_and_a_fragment_stays_within_the_cap() =>
        Prop.ForAll(Arb.From(ByteGenerators.Random(64 * 1024)), Arb.From(_caps), Holds);

    [Property]
    public Property Offsets_that_point_anywhere_never_throw() =>
        Prop.ForAll(Arb.From(_badOffsets), Arb.From(_caps), Holds);

    [Property]
    public Property Damaged_blocks_never_throw() =>
        Prop.ForAll(Arb.From(ByteGenerators.Damaged(ByteGenerators.Text.Select(HtmlFormat.Build))), Arb.From(_caps), Holds);

    [Property]
    public Property A_built_block_gives_its_fragment_back() =>
        Prop.ForAll(Arb.From(ByteGenerators.Text.Where(text => !text.Contains('\0', StringComparison.Ordinal))), fragment =>
        {
            var read = HtmlFormat.ExtractFragment(HtmlFormat.Build(fragment), 1 << 20);
            return read.IsSuccess && read.Value == fragment;
        });
}
