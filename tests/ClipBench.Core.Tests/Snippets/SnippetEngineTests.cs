using System.Globalization;
using ClipBench.Core.Results;
using ClipBench.Core.Snippets;

namespace ClipBench.Core.Tests.Snippets;

public class SnippetEngineTests
{
    // 21:30:45 UTC on Friday 23 October 2026 is 00:30:45 on Saturday 24 October at UTC+3, so the local zone matters.
    private static readonly DateTimeOffset _utc = new(2026, 10, 23, 21, 30, 45, TimeSpan.Zero);

    private readonly ZonedClock _clock = new(_utc, TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(3), "test", "test"));
    private readonly SnippetEngine _engine;

    public SnippetEngineTests()
    {
        _engine = new SnippetEngine(_clock);
    }

    private sealed class ZonedClock(DateTimeOffset utc, TimeZoneInfo zone) : TimeProvider
    {
        private DateTimeOffset _now = utc;

        public override TimeZoneInfo LocalTimeZone => zone;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static Dictionary<string, string> Inputs(params (string Name, string Value)[] inputs) =>
        inputs.ToDictionary(input => input.Name, input => input.Value);

    private static ParsedSnippet Parse(string body)
    {
        var parsed = SnippetEngine.Parse(body);
        parsed.IsSuccess.ShouldBeTrue(parsed.IsFailure ? parsed.Error.Message : null);
        return parsed.Value;
    }

    private string Run(string body, string? clipboard = null, Dictionary<string, string>? inputs = null)
    {
        var result = _engine.Expand(Parse(body), new SnippetValues(clipboard, inputs));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    // --- plain text ---

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("two\r\nlines\twith a tab")]
    [InlineData("Dear sir,\n\nKind regards")]
    [InlineData("\u0645\u0631\u062D\u0628\u0627 \U0001F600 e\u0301")]
    [InlineData("a } b")]
    [InlineData("a : b")]
    public void Text_without_braces_comes_out_as_it_went_in(string body)
    {
        Run(body).ShouldBe(body);
        Parse(body).Warnings.ShouldBeEmpty();
        Parse(body).InputNames.ShouldBeEmpty();
    }

    // --- date and time ---

    [Theory]
    [InlineData("{date}", "2026-10-24")]
    [InlineData("{time}", "00:30")]
    [InlineData("{date:dd/MM/yyyy}", "24/10/2026")]
    [InlineData("{date:yyyy}", "2026")]
    [InlineData("{time:HH:mm:ss}", "00:30:45")]
    [InlineData("{time:h:mm tt}", "12:30 AM")]
    [InlineData("{date:dddd, MMMM d}", "Saturday, October 24")]
    [InlineData("{DATE}", "2026-10-24")]
    [InlineData("{Time}", "00:30")]
    [InlineData("{date:'week' MM}", "week 10")]
    [InlineData("Today is {date} at {time}.", "Today is 2026-10-24 at 00:30.")]
    [InlineData("{date}{date}", "2026-10-242026-10-24")]
    public void The_date_and_the_time_are_those_of_the_local_zone(string body, string expected)
    {
        Run(body).ShouldBe(expected);
    }

    [Fact]
    public void The_date_does_not_depend_on_the_language_of_Windows()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            Run("{date} {date:dddd MMMM} {time}").ShouldBe("2026-10-24 Saturday October 00:30");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void The_clock_is_read_when_the_snippet_is_filled_in_and_not_when_it_is_read()
    {
        var parsed = Parse("{time:HH:mm}");

        _engine.Expand(parsed, new SnippetValues(null, null)).Value.ShouldBe("00:30");
        _clock.Advance(TimeSpan.FromMinutes(45));
        _engine.Expand(parsed, new SnippetValues(null, null)).Value.ShouldBe("01:15");
    }

    // --- the clipboard ---

    [Fact]
    public void The_clipboard_variable_is_the_text_given()
    {
        Run("Copied: {clipboard}!", clipboard: "line one\r\nline two").ShouldBe("Copied: line one\r\nline two!");
        Run("{CLIPBOARD}", clipboard: "x").ShouldBe("x");
    }

    [Fact]
    public void With_no_clipboard_text_the_variable_is_empty()
    {
        Run("[{clipboard}]", clipboard: null).ShouldBe("[]");
    }

    // --- typed values ---

    [Fact]
    public void An_input_is_the_value_typed_and_the_same_name_is_asked_once()
    {
        var parsed = Parse("Dear {input:Name}, {input:Topic}: {input:Name}.");

        parsed.InputNames.ShouldBe(["Name", "Topic"]);
        _engine.Expand(parsed, new SnippetValues(null, Inputs(("Name", "Sam"), ("Topic", "lunch")))).Value.ShouldBe("Dear Sam, lunch: Sam.");
    }

    [Fact]
    public void Names_of_inputs_are_the_same_whatever_the_capital_letters()
    {
        var parsed = Parse("{input:Name} {input:NAME}");

        parsed.InputNames.ShouldBe(["Name"]);
        _engine.Expand(parsed, new SnippetValues(null, Inputs(("name", "Sam")))).Value.ShouldBe("Sam Sam");
    }

    [Theory]
    [InlineData("{input:First name}", "First name")]
    [InlineData("{input:last-name}", "last-name")]
    [InlineData("{input:a_b2}", "a_b2")]
    [InlineData("{input:\u0627\u0644\u0627\u0633\u0645}", "\u0627\u0644\u0627\u0633\u0645")]
    [InlineData("{input: padded }", "padded")]
    public void A_name_may_have_letters_digits_spaces_hyphens_and_underscores(string body, string name)
    {
        Parse(body).InputNames.ShouldBe([name]);
        Parse(body).Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void A_name_can_be_as_long_as_the_limit_but_not_longer()
    {
        Parse("{input:" + new string('a', SnippetEngine.MaxInputNameLength) + "}").Warnings.ShouldBeEmpty();
        Parse("{input:" + new string('a', SnippetEngine.MaxInputNameLength + 1) + "}").Warnings.Single().Kind.ShouldBe(SnippetWarningKind.InvalidInputName);
    }

    [Fact]
    public void What_is_typed_may_hold_anything_and_is_not_read_again()
    {
        Run("{input:X}", inputs: Inputs(("X", "{date} {{ {input:X} }}"))).ShouldBe("{date} {{ {input:X} }}");
    }

    // --- one pass: nothing that is put in is ever read again ---

    [Fact]
    public void The_clipboard_cannot_bring_a_variable_with_it()
    {
        Run("{clipboard}", clipboard: "{date} {input:X} {clipboard}", inputs: Inputs(("X", "never"))).ShouldBe("{date} {input:X} {clipboard}");
    }

    [Fact]
    public void A_date_format_cannot_bring_a_variable_with_it()
    {
        // The format is quoted text, which comes out as written and is not read again.
        Run("{date:'{input:X'}", inputs: Inputs(("X", "never"))).ShouldBe("{input:X");
        Run("{date:'{'}").ShouldBe("{");
    }

    [Fact]
    public void A_snippet_cannot_expand_into_itself()
    {
        // The only way to put a snippet in a snippet is through the clipboard, and that is text and stays text.
        Run("{clipboard}{clipboard}", clipboard: "{clipboard}").ShouldBe("{clipboard}{clipboard}");
    }

    // --- braces ---

    [Theory]
    [InlineData("{{", "{")]
    [InlineData("}}", "}")]
    [InlineData("{{date}}", "{date}")]
    [InlineData("{{{date}}}", "{2026-10-24}")]
    [InlineData("a {{ b }} c", "a { b } c")]
    [InlineData("}}}", "}}")]
    [InlineData("{{{{", "{{")]
    [InlineData("function() {{ return 1; }}", "function() { return 1; }")]
    public void Doubled_braces_are_single_braces_and_never_start_a_variable(string body, string expected)
    {
        Run(body).ShouldBe(expected);
        Parse(body).Warnings.ShouldBeEmpty();
    }

    // --- things that are not understood are left alone and reported ---

    [Theory]
    [InlineData("{foo}", 0, 5)]
    [InlineData("a {foo:bar} b", 2, 9)]
    [InlineData("{}", 0, 2)]
    [InlineData("{ date }", 0, 8)]
    [InlineData("{clipboard:x}", 0, 13)]
    [InlineData("{ }", 0, 3)]
    [InlineData("{a {b}", 0, 6)]
    public void An_unknown_variable_is_left_as_it_is_with_a_warning_that_says_where(string body, int position, int length)
    {
        Run(body).ShouldBe(body);
        Parse(body).Warnings.ShouldBe([new SnippetWarning(SnippetWarningKind.UnknownVariable, position, length)]);
    }

    [Theory]
    [InlineData("{input}")]
    [InlineData("{input:}")]
    [InlineData("{input:   }")]
    [InlineData("{input:bad/name}")]
    [InlineData("{input:a{b}")]
    [InlineData("{input:name:more}")]
    public void An_input_without_a_usable_name_is_left_as_it_is_with_a_warning(string body)
    {
        Run(body).ShouldBe(body);
        var warning = Parse(body).Warnings.Single();
        warning.Kind.ShouldBe(SnippetWarningKind.InvalidInputName);
        warning.Position.ShouldBe(0);
        Parse(body).InputNames.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("{date:%}")]
    [InlineData("{time:\\}")]
    [InlineData("{date:}")]
    [InlineData("{date:ffffffff}")]
    public void A_format_that_cannot_be_used_leaves_the_variable_as_it_is_with_a_warning(string body)
    {
        Run(body).ShouldBe(body);
        Parse(body).Warnings.Single().Kind.ShouldBe(SnippetWarningKind.InvalidFormat);
    }

    [Fact]
    public void A_format_can_be_as_long_as_the_limit_but_not_longer()
    {
        var fits = "{date:" + new string('y', SnippetEngine.MaxFormatLength) + "}";
        var tooLong = "{date:" + new string('y', SnippetEngine.MaxFormatLength + 1) + "}";

        Parse(fits).Warnings.ShouldBeEmpty();
        Parse(tooLong).Warnings.Single().Kind.ShouldBe(SnippetWarningKind.InvalidFormat);
        Run(tooLong).ShouldBe(tooLong);
    }

    [Theory]
    [InlineData("abc {date", 4, 5)]
    [InlineData("{", 0, 1)]
    [InlineData("{input:Name", 0, 11)]
    [InlineData("ok {date} then {broken", 15, 7)]
    public void A_brace_that_is_never_closed_is_left_as_it_is_with_a_warning(string body, int position, int length)
    {
        Run(body).ShouldBe(body.Replace("{date}", "2026-10-24", StringComparison.Ordinal));
        Parse(body).Warnings[^1].ShouldBe(new SnippetWarning(SnippetWarningKind.UnclosedBrace, position, length));
    }

    [Fact]
    public void Warnings_come_in_the_order_they_appear_and_do_not_stop_the_good_variables()
    {
        var parsed = Parse("{nope} {date} {input:} {date:%} {time}");

        parsed.Warnings.Select(warning => warning.Kind).ShouldBe(
            [SnippetWarningKind.UnknownVariable, SnippetWarningKind.InvalidInputName, SnippetWarningKind.InvalidFormat]);
        _engine.Expand(parsed, new SnippetValues(null, null)).Value.ShouldBe("{nope} 2026-10-24 {input:} {date:%} 00:30");
    }

    // --- limits ---

    [Fact]
    public void A_snippet_can_be_as_long_as_the_limit_but_not_longer()
    {
        SnippetEngine.Parse(new string('a', SnippetEngine.MaxBodyLength)).IsSuccess.ShouldBeTrue();

        var tooLong = SnippetEngine.Parse(new string('a', SnippetEngine.MaxBodyLength + 1));

        tooLong.IsFailure.ShouldBeTrue();
        tooLong.Error.Code.ShouldBe(ErrorCode.InvalidSnippet);
    }

    [Fact]
    public void The_messages_write_numbers_the_same_way_whatever_the_language_of_Windows()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            SnippetEngine.Parse(new string('a', SnippetEngine.MaxBodyLength + 1)).Error.Message.ShouldBe("A snippet can have at most 10,000 characters.");
            _engine.Expand(Parse("{input:X}"), new SnippetValues(null, Inputs(("X", new string('a', SnippetEngine.MaxInputLength + 1))))).Error.Message
                .ShouldBe("A value can have at most 10,000 characters.");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_snippet_can_ask_for_so_many_values_but_not_more()
    {
        string Body(int count) => string.Concat(Enumerable.Range(0, count).Select(i => "{input:v" + i + "}"));

        SnippetEngine.Parse(Body(SnippetEngine.MaxInputs)).IsSuccess.ShouldBeTrue();

        var tooMany = SnippetEngine.Parse(Body(SnippetEngine.MaxInputs + 1));
        tooMany.IsFailure.ShouldBeTrue();
        tooMany.Error.Code.ShouldBe(ErrorCode.InvalidSnippet);
    }

    [Fact]
    public void Asking_for_the_same_value_many_times_counts_once()
    {
        SnippetEngine.Parse(string.Concat(Enumerable.Repeat("{input:X}", 500))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_missing_value_stops_the_snippet_and_says_which_kind_of_problem_it_is()
    {
        var parsed = Parse("{input:secret-marker}");

        var result = _engine.Expand(parsed, new SnippetValues(null, null));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.SnippetInputMissing);
        result.Error.Message.ShouldNotContain("secret-marker", Case.Sensitive, "the message must not repeat what the user typed");
    }

    [Fact]
    public void A_value_can_be_as_long_as_the_limit_but_not_longer()
    {
        var parsed = Parse("{input:X}");

        _engine.Expand(parsed, new SnippetValues(null, Inputs(("X", new string('a', SnippetEngine.MaxInputLength))))).IsSuccess.ShouldBeTrue();

        var tooLong = _engine.Expand(parsed, new SnippetValues(null, Inputs(("X", new string('a', SnippetEngine.MaxInputLength + 1)))));
        tooLong.Error.Code.ShouldBe(ErrorCode.SnippetInputTooLong);
    }

    [Fact]
    public void The_result_can_be_as_long_as_the_limit_but_not_longer()
    {
        var parsed = Parse("{clipboard}");

        _engine.Expand(parsed, new SnippetValues(new string('a', SnippetEngine.MaxOutputLength), null)).IsSuccess.ShouldBeTrue();

        var tooLong = _engine.Expand(parsed, new SnippetValues(new string('a', SnippetEngine.MaxOutputLength + 1), null));
        tooLong.Error.Code.ShouldBe(ErrorCode.OutputTooLarge);
    }

    [Fact]
    public void Repeating_a_long_value_cannot_make_a_result_over_the_limit()
    {
        var parsed = Parse(string.Concat(Enumerable.Repeat("{clipboard}", 100)));

        var result = _engine.Expand(parsed, new SnippetValues(new string('a', 20_000), null));

        result.Error.Code.ShouldBe(ErrorCode.OutputTooLarge);
    }

    // --- the parsed snippet ---

    [Fact]
    public void One_parsed_snippet_can_be_filled_in_again_and_again_with_other_values()
    {
        var parsed = Parse("{input:X}-{clipboard}");

        _engine.Expand(parsed, new SnippetValues("c1", Inputs(("X", "x1")))).Value.ShouldBe("x1-c1");
        _engine.Expand(parsed, new SnippetValues("c2", Inputs(("X", "x2")))).Value.ShouldBe("x2-c2");
    }

    [Fact]
    public void Printing_never_shows_the_snippet()
    {
        Parse("secret-marker {input:Name}").ToString().ShouldNotContain("secret-marker");
        new SnippetWarning(SnippetWarningKind.UnknownVariable, 3, 5).ToString().ShouldNotContain("secret-marker");
    }

    [Fact]
    public void Text_in_any_script_around_variables_is_kept()
    {
        Run("\u0645\u0631\u062D\u0628\u0627 {input:X} \U0001F600 {date}", inputs: Inputs(("X", "e\u0301"))).ShouldBe("\u0645\u0631\u062D\u0628\u0627 e\u0301 \U0001F600 2026-10-24");
    }

    [Fact]
    public void Missing_arguments_are_bugs()
    {
        Should.Throw<ArgumentNullException>(() => new SnippetEngine(null!));
        Should.Throw<ArgumentNullException>(() => SnippetEngine.Parse(null!));
        Should.Throw<ArgumentNullException>(() => _engine.Expand(null!, new SnippetValues(null, null)));
        Should.Throw<ArgumentNullException>(() => _engine.Expand(Parse("x"), null!));
    }
}
