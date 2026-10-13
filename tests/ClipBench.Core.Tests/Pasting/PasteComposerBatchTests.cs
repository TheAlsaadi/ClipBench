using ClipBench.Core.Clips;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Pasting;

public class PasteComposerBatchTests
{
    private readonly PasteComposer _composer = new();

    private static Clip[] Texts(params string[] texts) => [.. texts.Select(text => TestClips.Text(text))];

    [Theory]
    [InlineData("None", "abc")]
    [InlineData("NewLine", "a\r\nb\r\nc")]
    [InlineData("BlankLine", "a\r\n\r\nb\r\n\r\nc")]
    [InlineData("Space", "a b c")]
    [InlineData("Tab", "a\tb\tc")]
    [InlineData("Comma", "a,b,c")]
    [InlineData("CommaSpace", "a, b, c")]
    [InlineData("Semicolon", "a;b;c")]
    public void Each_separator_joins_the_clips(string name, string expected)
    {
        var result = _composer.ComposeBatch(Texts("a", "b", "c"), SeparatorNames.Get(name));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(expected);
    }

    [Fact]
    public void A_custom_separator_with_escape_sequences_is_used_as_typed()
    {
        var separator = Separator.Parse(@" | \n").Value;

        _composer.ComposeBatch(Texts("a", "b"), separator).Value.ShouldBe("a | \nb");
    }

    [Fact]
    public void The_order_of_the_selection_is_kept()
    {
        _composer.ComposeBatch(Texts("third", "first", "second"), Separator.Comma).Value.ShouldBe("third,first,second");
    }

    [Fact]
    public void A_single_clip_is_returned_without_a_separator()
    {
        _composer.ComposeBatch(Texts("only"), Separator.NewLine).Value.ShouldBe("only");
    }

    [Fact]
    public void An_empty_clip_still_takes_its_place_in_the_list()
    {
        _composer.ComposeBatch(Texts("a", "", "c"), Separator.Comma).Value.ShouldBe("a,,c");
    }

    [Fact]
    public void Rich_text_clips_contribute_their_plain_text()
    {
        var html = new Clip(ClipId.New(), new HtmlContent("<b>bold</b>", "bold"), TestClips.Start);
        var rtf = new Clip(ClipId.New(), new RtfContent(@"{\rtf1 rich}", "rich"), TestClips.Start);

        _composer.ComposeBatch([html, rtf, TestClips.Text("plain")], Separator.Space).Value.ShouldBe("bold rich plain");
    }

    [Fact]
    public void A_file_clip_contributes_its_paths_one_per_line()
    {
        var files = TestClips.Files(null, @"C:\a.txt", @"C:\b.txt");

        _composer.ComposeBatch([files], Separator.Comma).Value.ShouldBe("C:\\a.txt\r\nC:\\b.txt");
    }

    [Fact]
    public void A_mix_of_text_and_images_is_rejected_because_images_cannot_be_joined_into_text()
    {
        var result = _composer.ComposeBatch([TestClips.Text("secret-marker"), TestClips.Image(1)], Separator.NewLine);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.ImageNotJoinable);
        result.Error.Message.ShouldContain("image", Case.Insensitive);
        result.Error.Message.ShouldNotContain("secret-marker");
    }

    [Fact]
    public void Images_alone_are_rejected_too()
    {
        _composer.ComposeBatch([TestClips.Image(1), TestClips.Image(2)], Separator.NewLine).Error.Code
            .ShouldBe(ErrorCode.ImageNotJoinable);
    }

    [Fact]
    public void Nothing_selected_is_an_error_not_an_empty_paste()
    {
        _composer.ComposeBatch([], Separator.Comma).Error.Code.ShouldBe(ErrorCode.NothingSelected);
    }

    [Fact]
    public void A_result_larger_than_the_limit_is_rejected_before_it_is_built()
    {
        var composer = new PasteComposer(maxOutputCharacters: 10);

        composer.ComposeBatch(Texts("12345", "67890"), Separator.None).IsSuccess.ShouldBeTrue();

        var tooBig = composer.ComposeBatch(Texts("12345", "67890"), Separator.Comma);

        tooBig.IsFailure.ShouldBeTrue();
        tooBig.Error.Code.ShouldBe(ErrorCode.OutputTooLarge);
    }

    [Fact]
    public void The_default_limit_allows_ten_million_characters()
    {
        PasteComposer.DefaultMaxOutputCharacters.ShouldBe(10_000_000);
    }

    [Fact]
    public void A_limit_below_one_is_a_bug()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new PasteComposer(maxOutputCharacters: 0));
    }

    [Fact]
    public void Missing_arguments_are_bugs()
    {
        Should.Throw<ArgumentNullException>(() => _composer.ComposeBatch(null!, Separator.Comma));
        Should.Throw<ArgumentNullException>(() => _composer.ComposeBatch(Texts("a"), null!));
    }
}
