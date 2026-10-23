using System.Diagnostics;
using ClipBench.Core.Results;
using ClipBench.Core.Transforms;

namespace ClipBench.Core.Tests.Transforms;

public class FindReplaceTests
{
    private const string Arabic = "\u0645\u0631\u062D\u0628\u0627";
    private const string Emoji = "\U0001F600";

    private static TextTransform Build(string find, string replace, bool regex = false, bool ignoreCase = false, TimeSpan? timeout = null, int maxCharacters = 10_000_000)
    {
        var created = FindReplace.Create(new FindReplaceOptions { Find = find, Replace = replace, UseRegex = regex, IgnoreCase = ignoreCase }, timeout, maxCharacters);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : null);
        return created.Value;
    }

    private static string Plain(string text, string find, string replace, bool ignoreCase = false)
    {
        var result = Build(find, replace, regex: false, ignoreCase).Apply(text);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private static string Pattern(string text, string pattern, string replace, bool ignoreCase = false)
    {
        var result = Build(pattern, replace, regex: true, ignoreCase).Apply(text);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    [Theory]
    [InlineData("a-b-c", "-", "+", "a+b+c")]
    [InlineData("a-b-c", "-", "", "abc")]
    [InlineData("Hello hello HELLO", "hello", "x", "Hello x HELLO")]
    [InlineData("aaa", "aa", "b", "ba")]
    [InlineData("nothing here", "zzz", "x", "nothing here")]
    [InlineData("", "a", "b", "")]
    [InlineData("a", "longer than the text", "x", "a")]
    [InlineData("price $5", "$5", "$1", "price $1")]
    [InlineData("a.b", ".", "!", "a!b")]
    [InlineData("1+1=2", "+", " plus ", "1 plus 1=2")]
    [InlineData("line one\r\nline two", "\r\n", " | ", "line one | line two")]
    public void Plain_find_replaces_every_occurrence_exactly_as_typed(string text, string find, string replace, string expected)
    {
        Plain(text, find, replace).ShouldBe(expected);
    }

    [Fact]
    public void Plain_find_can_ignore_case()
    {
        Plain("Hello hello HELLO", "hello", "x", ignoreCase: true).ShouldBe("x x x");
    }

    [Fact]
    public void Ignoring_case_uses_the_invariant_culture_so_Turkish_letters_do_not_match()
    {
        Plain("I \u0131 \u0130 i", "i", "X", ignoreCase: true).ShouldBe("X \u0131 \u0130 X");
    }

    [Fact]
    public void Plain_find_works_with_text_in_any_script()
    {
        Plain(Arabic + " " + Arabic, Arabic, "ok").ShouldBe("ok ok");
        Plain("a" + Emoji + "b" + Emoji, Emoji, "!").ShouldBe("a!b!");
        Plain("e\u0301 e", "e", "x").ShouldBe("x\u0301 x");
    }

    [Theory]
    [InlineData("a1 b22 c333", @"\d+", "#", "a# b# c#")]
    [InlineData("ann@home bob@work", @"(\w+)@(\w+)", "$2:$1", "home:ann work:bob")]
    [InlineData("ann@home", @"(?<user>\w+)@(?<host>\w+)", "${host}/${user}", "home/ann")]
    [InlineData("a b", @"\s", "", "ab")]
    [InlineData("abc", @"b", "[$&]", "a[b]c")]
    [InlineData("ab\ncd", @".+", "X", "X\nX")]
    [InlineData("abc", @"(?<=a)b", "X", "aXc")]
    [InlineData("abab ab", @"(ab)\1", "X", "X ab")]
    [InlineData("a1b2", @"\p{L}", "_", "_1_2")]
    public void Regex_find_replaces_every_match_with_groups_available(string text, string pattern, string replace, string expected)
    {
        Pattern(text, pattern, replace).ShouldBe(expected);
    }

    [Fact]
    public void Regex_find_can_ignore_case()
    {
        Pattern("Hello hello", "HELLO", "x", ignoreCase: true).ShouldBe("x x");
    }

    [Fact]
    public void Regex_find_reads_letters_of_any_script_as_letters()
    {
        Pattern(Arabic + " 12", @"\p{L}+", "word").ShouldBe("word 12");
    }

    [Fact]
    public void Caret_and_dollar_match_at_each_line()
    {
        Pattern("a\nb", "^", "- ").ShouldBe("- a\n- b");
        Pattern("a\nb", "$", ";").ShouldBe("a;\n" + "b;");
    }

    [Fact]
    public void Dollar_matches_before_a_Windows_line_break_too()
    {
        Pattern("a\r\nb\r\n", "$", ";").ShouldBe("a;\r\nb;\r\n;");
        Pattern("a\r\nb", "^", "- ").ShouldBe("- a\r\n- b");
    }

    [Fact]
    public void A_pattern_that_would_take_forever_on_a_backtracking_engine_finishes()
    {
        var text = new string('a', 200_000) + "!";
        var watch = Stopwatch.StartNew();

        Pattern(text, "(a+)+$", "x").ShouldBe(text);

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_pattern_that_needs_backtracking_and_takes_too_long_fails_instead_of_hanging()
    {
        var transform = Build("^(a+)+$(?!x)", "x", regex: true, timeout: TimeSpan.FromMilliseconds(100));

        var result = transform.Apply(new string('a', 45) + "!");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.PatternTimedOut);
    }

    [Theory]
    [InlineData("(")]
    [InlineData("[a-")]
    [InlineData("*a")]
    [InlineData("a{2,1}")]
    [InlineData("(?<n>a)(?<n")]
    public void An_invalid_pattern_is_refused_without_repeating_it(string pattern)
    {
        var created = FindReplace.Create(new FindReplaceOptions { Find = pattern + "secret-marker", Replace = "x", UseRegex = true });

        created.IsFailure.ShouldBeTrue();
        created.Error.Code.ShouldBe(ErrorCode.PatternInvalid);
        created.Error.Message.ShouldNotContain("secret-marker");
    }

    [Fact]
    public void Nothing_to_find_is_refused()
    {
        foreach (var regex in new[] { false, true })
        {
            var created = FindReplace.Create(new FindReplaceOptions { Find = string.Empty, Replace = "x", UseRegex = regex });

            created.IsFailure.ShouldBeTrue();
            created.Error.Code.ShouldBe(ErrorCode.PatternInvalid);
        }
    }

    [Fact]
    public void The_pattern_and_the_replacement_have_a_length_limit()
    {
        FindReplace.Create(new FindReplaceOptions { Find = new string('a', FindReplace.MaxPatternLength), Replace = "x" }).IsSuccess.ShouldBeTrue();
        FindReplace.Create(new FindReplaceOptions { Find = new string('a', FindReplace.MaxPatternLength + 1), Replace = "x" }).Error.Code.ShouldBe(ErrorCode.PatternInvalid);
        FindReplace.Create(new FindReplaceOptions { Find = new string('a', FindReplace.MaxPatternLength + 1), Replace = "x", UseRegex = true }).Error.Code.ShouldBe(ErrorCode.PatternInvalid);
        FindReplace.Create(new FindReplaceOptions { Find = "a", Replace = new string('x', FindReplace.MaxReplacementLength) }).IsSuccess.ShouldBeTrue();
        FindReplace.Create(new FindReplaceOptions { Find = "a", Replace = new string('x', FindReplace.MaxReplacementLength + 1) }).Error.Code.ShouldBe(ErrorCode.PatternInvalid);
    }

    [Theory]
    [InlineData(false, "a")]
    [InlineData(true, "a")]
    public void A_result_over_the_limit_fails_before_it_is_built(bool regex, string find)
    {
        var transform = Build(find, "bb", regex, maxCharacters: 100);

        var result = transform.Apply(new string('a', 60));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.OutputTooLarge);
    }

    [Fact]
    public void A_result_of_exactly_the_limit_is_accepted()
    {
        Build("a", "bb", maxCharacters: 100).Apply(new string('a', 50)).Value.Length.ShouldBe(100);
    }

    [Fact]
    public void Large_text_is_replaced()
    {
        var text = string.Concat(Enumerable.Repeat("one two\n", 600_000));

        Plain(text, "one", "1").Length.ShouldBe(text.Length - (2 * 600_000));
        Pattern(text, @"\btwo\b", "2").Length.ShouldBe(text.Length - (2 * 600_000));
    }

    [Fact]
    public void Printing_the_options_never_shows_what_was_typed()
    {
        new FindReplaceOptions { Find = "secret-marker", Replace = "other-marker" }.ToString().ShouldNotContain("marker");
    }

    [Fact]
    public void The_transform_has_a_stable_identifier_and_a_name()
    {
        var transform = Build("a", "b");

        transform.Id.ShouldBe("text.find-replace");
        transform.Name.ShouldBe("Find and replace");
    }

    [Fact]
    public void Missing_arguments_are_bugs()
    {
        Should.Throw<ArgumentNullException>(() => FindReplace.Create(null!));
        Should.Throw<ArgumentNullException>(() => new FindReplaceOptions { Find = null! });
        Should.Throw<ArgumentNullException>(() => new FindReplaceOptions { Replace = null! });
        Should.Throw<ArgumentOutOfRangeException>(() => FindReplace.Create(new FindReplaceOptions { Find = "a" }, maxCharacters: 0));
        Should.Throw<ArgumentOutOfRangeException>(() => FindReplace.Create(new FindReplaceOptions { Find = "a" }, timeout: TimeSpan.Zero));
    }
}
