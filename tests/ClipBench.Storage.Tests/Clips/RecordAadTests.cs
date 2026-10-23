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
    public void A_board_value_is_bound_to_a_tag_its_id_and_its_column()
    {
        var id = new ClipBench.Core.Boards.BoardId(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
        byte[] expected =
        [
            .. "CB1B"u8,
            0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
            4, .. "name"u8,
        ];

        RecordAad.ForBoard(id, "name").ShouldBe(expected);
    }

    [Fact]
    public void A_snippet_value_is_bound_to_a_tag_its_id_and_its_column()
    {
        var id = new ClipBench.Core.Snippets.SnippetId(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
        byte[] expected =
        [
            .. "CB1S"u8,
            0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
            4, .. "body"u8,
        ];

        RecordAad.ForSnippet(id, "body").ShouldBe(expected);
    }

    [Fact]
    public void A_snippet_value_can_never_be_taken_for_a_board_or_clip_value_and_depends_on_id_and_column()
    {
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var snippet = new ClipBench.Core.Snippets.SnippetId(guid);

        RecordAad.ForSnippet(snippet, "name").ShouldNotBe(RecordAad.ForBoard(new ClipBench.Core.Boards.BoardId(guid), "name"));
        RecordAad.ForSnippet(snippet, "name").ShouldNotBe(RecordAad.ForClip(new ClipId(guid), "name", ClipFormat.Text));
        RecordAad.ForSnippet(snippet, "body").ShouldNotBe(RecordAad.ForSnippet(snippet, "name"));
        RecordAad.ForSnippet(new ClipBench.Core.Snippets.SnippetId(Guid.NewGuid()), "name").ShouldNotBe(RecordAad.ForSnippet(snippet, "name"));
    }

    [Fact]
    public void A_board_value_can_never_be_taken_for_a_clip_value_even_with_the_same_id_and_column()
    {
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        RecordAad.ForBoard(new ClipBench.Core.Boards.BoardId(guid), "preview")
            .ShouldNotBe(RecordAad.ForClip(new ClipId(guid), "preview", ClipFormat.Text));
    }

    [Fact]
    public void Another_board_id_or_column_gives_other_board_data()
    {
        var id = new ClipBench.Core.Boards.BoardId(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
        var other = new ClipBench.Core.Boards.BoardId(Guid.Parse("00112233-4455-6677-8899-aabbccddee00"));

        RecordAad.ForBoard(other, "name").ShouldNotBe(RecordAad.ForBoard(id, "name"));
        RecordAad.ForBoard(id, "other").ShouldNotBe(RecordAad.ForBoard(id, "name"));
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
