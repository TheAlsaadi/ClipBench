using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Pasting;

public class SeparatorTests
{
    [Theory]
    [InlineData("None", "")]
    [InlineData("NewLine", "\r\n")]
    [InlineData("BlankLine", "\r\n\r\n")]
    [InlineData("Space", " ")]
    [InlineData("Tab", "\t")]
    [InlineData("Comma", ",")]
    [InlineData("CommaSpace", ", ")]
    [InlineData("Semicolon", ";")]
    public void Each_preset_has_the_text_it_is_named_for(string name, string expected)
    {
        SeparatorNames.Get(name).Text.ShouldBe(expected);
    }

    [Theory]
    [InlineData(@"\n", "\n")]
    [InlineData(@"\r", "\r")]
    [InlineData(@"\t", "\t")]
    [InlineData(@"\r\n", "\r\n")]
    [InlineData(@"\\", "\\")]
    [InlineData(@" | ", " | ")]
    [InlineData(@"; \n", "; \n")]
    [InlineData(@"\t\t", "\t\t")]
    [InlineData("", "")]
    public void A_custom_separator_understands_escape_sequences(string typed, string expected)
    {
        var result = Separator.Parse(typed);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Text.ShouldBe(expected);
    }

    [Theory]
    [InlineData(@"\q")]
    [InlineData(@"abc\")]
    [InlineData(@"\")]
    [InlineData(@"\x41")]
    public void An_unknown_or_unfinished_escape_sequence_is_rejected_with_a_clear_error(string typed)
    {
        var result = Separator.Parse(typed);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.InvalidSeparator);
        result.Error.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_custom_separator_has_a_length_limit()
    {
        Separator.Parse(new string('-', Separator.MaxCustomLength)).IsSuccess.ShouldBeTrue();

        var tooLong = Separator.Parse(new string('-', Separator.MaxCustomLength + 1));

        tooLong.IsFailure.ShouldBeTrue();
        tooLong.Error.Code.ShouldBe(ErrorCode.InvalidSeparator);
    }

    [Fact]
    public void The_length_limit_counts_the_text_after_escapes_are_resolved()
    {
        // 64 escape pairs resolve to 64 characters, which is allowed; the typed text is 128 characters long.
        Separator.Parse(string.Concat(Enumerable.Repeat(@"\t", Separator.MaxCustomLength))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Separators_with_the_same_text_are_equal()
    {
        Separator.Parse(@"\t").Value.ShouldBe(Separator.Tab);
        Separator.Comma.ShouldNotBe(Separator.CommaSpace);
    }

    [Fact]
    public void A_missing_separator_text_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => Separator.Parse(null!));
    }
}
