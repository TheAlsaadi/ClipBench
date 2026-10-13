using System.Text;
using ClipBench.Core.Clips;
using ClipBench.Storage.Clips;

namespace ClipBench.Storage.Tests.Clips;

public class RecordAadTests
{
    private static readonly ClipId _id = new(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));

    [Fact]
    public void A_clip_value_is_bound_to_a_tag_its_id_its_column_and_its_format()
    {
        byte[] expected =
        [
            .. "CB1C"u8,
            0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
            7, .. "content"u8,
            (byte)ClipFormat.Html,
        ];

        RecordAad.ForClip(_id, "content", ClipFormat.Html).ShouldBe(expected);
    }

    [Fact]
    public void A_meta_value_is_bound_to_a_tag_and_its_name()
    {
        RecordAad.ForMeta("key_check").ShouldBe([.. "CB1M"u8, 9, .. "key_check"u8]);
    }

    [Fact]
    public void Another_id_gives_other_data()
    {
        var other = new ClipId(Guid.Parse("00112233-4455-6677-8899-aabbccddee00"));

        RecordAad.ForClip(other, "content", ClipFormat.Text).ShouldNotBe(RecordAad.ForClip(_id, "content", ClipFormat.Text));
    }

    [Fact]
    public void Another_column_gives_other_data()
    {
        RecordAad.ForClip(_id, "preview", ClipFormat.Text).ShouldNotBe(RecordAad.ForClip(_id, "content", ClipFormat.Text));
    }

    [Fact]
    public void Another_format_gives_other_data()
    {
        RecordAad.ForClip(_id, "content", ClipFormat.Html).ShouldNotBe(RecordAad.ForClip(_id, "content", ClipFormat.Rtf));
    }

    [Fact]
    public void A_clip_value_and_a_meta_value_never_share_data()
    {
        // A meta row named like a column must still differ, so a key check cannot stand in for a clip value.
        RecordAad.ForMeta("content").ShouldNotBe(RecordAad.ForClip(_id, "content", ClipFormat.Text));
        Encoding.ASCII.GetString(RecordAad.ForMeta("x")[..4]).ShouldNotBe(Encoding.ASCII.GetString(RecordAad.ForClip(_id, "x", ClipFormat.Text)[..4]));
    }

    [Fact]
    public void An_empty_column_or_name_is_a_bug()
    {
        Should.Throw<ArgumentException>(() => RecordAad.ForClip(_id, string.Empty, ClipFormat.Text));
        Should.Throw<ArgumentException>(() => RecordAad.ForMeta(string.Empty));
    }
}
