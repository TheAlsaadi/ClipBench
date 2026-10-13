using ClipBench.Core.Clips;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Pasting;

public class PasteComposerTableTests
{
    private readonly PasteComposer _composer = new();

    private static Clip[] Texts(params string[] texts) => [.. texts.Select(text => TestClips.Text(text))];

    [Fact]
    public void A_column_puts_each_clip_on_its_own_line()
    {
        _composer.ComposeTable(Texts("a", "b", "c"), TableLayout.Column).Value.ShouldBe("a\r\nb\r\nc");
    }

    [Fact]
    public void A_row_puts_the_clips_side_by_side_separated_by_tabs()
    {
        _composer.ComposeTable(Texts("a", "b", "c"), TableLayout.Row).Value.ShouldBe("a\tb\tc");
    }

    [Theory]
    [InlineData(TableLayout.Column)]
    [InlineData(TableLayout.Row)]
    public void A_single_clip_is_a_single_cell(TableLayout layout)
    {
        _composer.ComposeTable(Texts("only"), layout).Value.ShouldBe("only");
    }

    [Fact]
    public void The_order_of_the_selection_is_kept()
    {
        _composer.ComposeTable(Texts("z", "a", "m"), TableLayout.Column).Value.ShouldBe("z\r\na\r\nm");
    }

    [Theory]
    [InlineData("x\ty", "\"x\ty\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("line1\r\nline2", "\"line1\r\nline2\"")]
    [InlineData("carriage\rreturn", "\"carriage\rreturn\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("\"", "\"\"\"\"")]
    public void A_cell_with_a_tab_a_line_break_or_a_quote_is_quoted_the_way_Excel_expects(string cell, string expected)
    {
        _composer.ComposeTable(Texts(cell), TableLayout.Row).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("with, comma")]
    [InlineData("with ; semicolon")]
    [InlineData("  padded  ")]
    [InlineData("")]
    public void A_cell_without_a_tab_line_break_or_quote_is_left_alone(string cell)
    {
        _composer.ComposeTable(Texts(cell), TableLayout.Row).Value.ShouldBe(cell);
    }

    [Fact]
    public void Quoted_cells_do_not_disturb_their_neighbours_in_a_row()
    {
        _composer.ComposeTable(Texts("a", "b\tc", "d"), TableLayout.Row).Value.ShouldBe("a\t\"b\tc\"\td");
    }

    [Fact]
    public void A_multi_line_cell_stays_one_cell_in_a_column()
    {
        _composer.ComposeTable(Texts("line1\nline2", "x"), TableLayout.Column).Value.ShouldBe("\"line1\nline2\"\r\nx");
    }

    [Fact]
    public void An_empty_cell_keeps_its_place()
    {
        _composer.ComposeTable(Texts("a", "", "c"), TableLayout.Row).Value.ShouldBe("a\t\tc");
    }

    [Fact]
    public void Rich_text_clips_contribute_their_plain_text()
    {
        var html = new Clip(ClipId.New(), new HtmlContent("<b>bold</b>", "bold"), TestClips.Start);

        _composer.ComposeTable([html, TestClips.Text("plain")], TableLayout.Row).Value.ShouldBe("bold\tplain");
    }

    [Fact]
    public void A_file_clip_is_one_quoted_cell_because_its_paths_span_lines()
    {
        var files = TestClips.Files(null, @"C:\a.txt", @"C:\b.txt");

        _composer.ComposeTable([files], TableLayout.Row).Value.ShouldBe("\"C:\\a.txt\r\nC:\\b.txt\"");
    }

    [Fact]
    public void Images_cannot_be_cells()
    {
        var result = _composer.ComposeTable([TestClips.Text("a"), TestClips.Image(1)], TableLayout.Column);

        result.Error.Code.ShouldBe(ErrorCode.ImageNotJoinable);
    }

    [Fact]
    public void Nothing_selected_is_an_error()
    {
        _composer.ComposeTable([], TableLayout.Row).Error.Code.ShouldBe(ErrorCode.NothingSelected);
    }

    [Fact]
    public void A_table_larger_than_the_limit_is_rejected()
    {
        var composer = new PasteComposer(maxOutputCharacters: 10);

        composer.ComposeTable(Texts("12345", "67890"), TableLayout.Column).Error.Code.ShouldBe(ErrorCode.OutputTooLarge);
    }

    [Theory]
    [InlineData(TableLayout.Column, 12)]
    [InlineData(TableLayout.Row, 11)]
    public void A_table_exactly_at_the_limit_is_allowed_and_one_character_more_is_not(TableLayout layout, int length)
    {
        var clips = Texts("12345", "67890");

        new PasteComposer(maxOutputCharacters: length).ComposeTable(clips, layout).IsSuccess.ShouldBeTrue();
        new PasteComposer(maxOutputCharacters: length - 1).ComposeTable(clips, layout).Error.Code.ShouldBe(ErrorCode.OutputTooLarge);
    }

    [Fact]
    public void A_table_whose_quoting_brings_it_exactly_to_the_limit_is_allowed()
    {
        // Two quote marks become four plus the two wrapping quotes: 6 characters.
        new PasteComposer(maxOutputCharacters: 6).ComposeTable(Texts("\"\""), TableLayout.Row).Value.ShouldBe("\"\"\"\"\"\"");
    }

    [Fact]
    public void Quoting_that_pushes_the_table_over_the_limit_is_rejected_too()
    {
        // Five quote marks become ten, plus the two wrapping quotes: 12 characters in a 10 character budget.
        var composer = new PasteComposer(maxOutputCharacters: 10);

        composer.ComposeTable(Texts("\"\"\"\"\""), TableLayout.Row).Error.Code.ShouldBe(ErrorCode.OutputTooLarge);
    }

    [Fact]
    public void An_unknown_layout_and_missing_arguments_are_bugs()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _composer.ComposeTable(Texts("a"), (TableLayout)99));
        Should.Throw<ArgumentNullException>(() => _composer.ComposeTable(null!, TableLayout.Row));
    }
}
