using ClipBench.Core.Boards;
using ClipBench.Core.Results;

namespace ClipBench.Core.Tests.Boards;

public class BoardNameTests
{
    [Theory]
    [InlineData("Work", "Work")]
    [InlineData("  Work  ", "Work")]
    [InlineData("\tWork\r\n", "Work")]
    [InlineData("Weekly report 2026", "Weekly report 2026")]
    [InlineData("a", "a")]
    [InlineData("\u0645\u0631\u062D\u0628\u0627", "\u0645\u0631\u062D\u0628\u0627")]
    [InlineData("Plans \U0001F680", "Plans \U0001F680")]
    [InlineData("caf\u00E9 e\u0301", "caf\u00E9 e\u0301")]
    [InlineData("with  inner   spaces", "with  inner   spaces")]
    public void A_name_is_trimmed_and_kept_as_typed(string typed, string expected)
    {
        var result = BoardName.Validate(typed);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("two\nlines")]
    [InlineData("tab\tinside")]
    [InlineData("nul\0inside")]
    [InlineData("bell\u0007")]
    public void An_empty_name_or_one_with_control_characters_is_refused(string typed)
    {
        var result = BoardName.Validate(typed);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.InvalidBoardName);
        result.Error.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_name_with_half_of_a_surrogate_pair_is_refused_because_it_could_not_be_saved()
    {
        // Built here: a lone surrogate does not survive being passed through the test runner as data.
        var result = BoardName.Validate("a" + (char)0xD800 + "b");

        result.Error.Code.ShouldBe(ErrorCode.InvalidBoardName);
    }

    [Fact]
    public void A_name_can_be_as_long_as_the_limit_but_not_longer()
    {
        BoardName.Validate(new string('a', BoardName.MaxLength)).IsSuccess.ShouldBeTrue();
        BoardName.Validate(new string('a', BoardName.MaxLength + 1)).Error.Code.ShouldBe(ErrorCode.InvalidBoardName);
    }

    [Fact]
    public void The_length_is_counted_after_trimming()
    {
        BoardName.Validate("  " + new string('a', BoardName.MaxLength) + "  ").IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_refusal_does_not_repeat_the_name()
    {
        BoardName.Validate("secret-marker\ninside").Error.Message.ShouldNotContain("secret-marker");
    }

    [Fact]
    public void A_missing_name_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => BoardName.Validate(null!));
    }

    [Fact]
    public void A_board_prints_its_id_and_never_its_name()
    {
        var board = new Board(BoardId.New(), "secret-marker", 0);

        board.ToString().ShouldNotContain("secret-marker");
        board.ToString().ShouldContain(board.Id.Value.ToString("N"));
    }

    [Fact]
    public void A_board_needs_a_name()
    {
        Should.Throw<ArgumentNullException>(() => new Board(BoardId.New(), null!, 0));
    }

    [Fact]
    public void New_board_ids_are_different()
    {
        BoardId.New().ShouldNotBe(BoardId.New());
    }
}
