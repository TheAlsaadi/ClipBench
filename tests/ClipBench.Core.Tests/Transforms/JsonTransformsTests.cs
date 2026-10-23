using ClipBench.Core.Results;
using ClipBench.Core.Transforms;

namespace ClipBench.Core.Tests.Transforms;

public class JsonTransformsTests
{
    // Single quotes stand for double quotes, so the JSON in the tests can be read.
    private static string Q(string text) => text.Replace('\'', '"');

    private static string Lines(params string[] lines) => string.Join("\r\n", lines.Select(Q));

    private static string Run(TextTransform transform, string text)
    {
        var result = transform.Apply(text);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private static void ShouldFail(TextTransform transform, string text)
    {
        var result = transform.Apply(text);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.TransformInputInvalid);
        result.Error.Message.ShouldNotBeNullOrWhiteSpace();
    }

    private static string Nested(int depth) => new string('[', depth) + new string(']', depth);

    [Fact]
    public void Format_lays_objects_and_arrays_out_with_two_space_indents()
    {
        var formatted = Run(BuiltInTransforms.JsonFormat, Q("{'a':1,'b':[1,2,{'c':null}],'d':true}"));

        formatted.ShouldBe(Lines(
            "{",
            "  'a': 1,",
            "  'b': [",
            "    1,",
            "    2,",
            "    {",
            "      'c': null",
            "    }",
            "  ],",
            "  'd': true",
            "}"));
    }

    [Theory]
    [InlineData("{}", "{}")]
    [InlineData("[]", "[]")]
    [InlineData("  { }  ", "{}")]
    [InlineData("123", "123")]
    [InlineData("'text'", "'text'")]
    [InlineData("true", "true")]
    [InlineData("null", "null")]
    public void Format_keeps_empty_containers_and_single_values_on_one_line(string json, string expected)
    {
        Run(BuiltInTransforms.JsonFormat, Q(json)).ShouldBe(Q(expected));
    }

    [Fact]
    public void Format_is_the_same_whether_the_input_was_minified_or_not()
    {
        var spaced = Q("{ 'a' : [ 1 , 2 ] ,\r\n\t'b' : { 'c' : 'd' } }");
        var minified = Q("{'a':[1,2],'b':{'c':'d'}}");

        Run(BuiltInTransforms.JsonFormat, spaced).ShouldBe(Run(BuiltInTransforms.JsonFormat, minified));
    }

    [Fact]
    public void Format_keeps_numbers_exactly_as_they_were_written()
    {
        var json = "[1.50,1E3,-0,123456789012345678901234567890,0.1000000000000000055511151231257827]";

        Run(BuiltInTransforms.JsonFormat, json).ShouldBe(Lines(
            "[",
            "  1.50,",
            "  1E3,",
            "  -0,",
            "  123456789012345678901234567890,",
            "  0.1000000000000000055511151231257827",
            "]"));
    }

    [Fact]
    public void Format_keeps_the_order_of_properties_and_repeated_names()
    {
        Run(BuiltInTransforms.JsonMinify, Q("{'b':1,'a':2,'b':3}")).ShouldBe(Q("{'b':1,'a':2,'b':3}"));
    }

    [Fact]
    public void Format_keeps_text_in_any_script_unescaped()
    {
        var json = Q("{'name':'caf\u00E9 \u0645\u0631\u062D\u0628\u0627 \U0001F600'}");

        Run(BuiltInTransforms.JsonMinify, json).ShouldBe(json);
        Run(BuiltInTransforms.JsonFormat, json).ShouldBe(Lines("{", "  'name': 'caf\u00E9 \u0645\u0631\u062D\u0628\u0627 \U0001F600'", "}"));
    }

    [Fact]
    public void Format_keeps_escapes_that_JSON_needs()
    {
        var json = @"{""a"":""line\nbreak \""quoted\"" back\\slash\ttab""}";

        Run(BuiltInTransforms.JsonMinify, json).ShouldBe(json);
    }

    [Fact]
    public void Format_does_not_turn_angle_brackets_and_ampersands_into_escapes()
    {
        var json = Q("{'html':'<b>&amp; a+b</b>'}");

        Run(BuiltInTransforms.JsonMinify, json).ShouldBe(json);
    }

    [Fact]
    public void A_byte_order_mark_at_the_start_is_ignored()
    {
        Run(BuiltInTransforms.JsonMinify, "\uFEFF{ \"a\": 1 }").ShouldBe(Q("{'a':1}"));
    }

    [Fact]
    public void Minify_removes_all_white_space_between_tokens_and_keeps_it_inside_strings()
    {
        var json = Q("{\r\n  'a' : [ 1, 2 ],\r\n  'b' : 'x  y'\r\n}");

        Run(BuiltInTransforms.JsonMinify, json).ShouldBe(Q("{'a':[1,2],'b':'x  y'}"));
    }

    [Fact]
    public void Minify_of_formatted_JSON_gives_back_the_minified_form()
    {
        var minified = Q("{'a':[1,2,{'c':null}],'d':'e'}");

        Run(BuiltInTransforms.JsonMinify, Run(BuiltInTransforms.JsonFormat, minified)).ShouldBe(minified);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("{'a':}")]
    [InlineData("{'a':1,}")]
    [InlineData("[1,2,]")]
    [InlineData("{a:1}")]
    [InlineData("{'a':1} {'b':2}")]
    [InlineData("{'a':1} trailing")]
    [InlineData("// comment\r\n{}")]
    [InlineData("{ /* c */ }")]
    [InlineData("NaN")]
    [InlineData("hello")]
    [InlineData("{'a':'\\x'}")]
    [InlineData("[01]")]
    public void Text_that_is_not_JSON_fails_for_both_transforms(string json)
    {
        ShouldFail(BuiltInTransforms.JsonFormat, Q(json));
        ShouldFail(BuiltInTransforms.JsonMinify, Q(json));
    }

    [Fact]
    public void The_error_says_which_line_went_wrong_but_never_repeats_the_text()
    {
        var result = BuiltInTransforms.JsonFormat.Apply("{\r\n  \"secret-marker\": ,\r\n}");

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("line 2");
        result.Error.Message.ShouldNotContain("secret-marker");
    }

    [Fact]
    public void Nesting_up_to_the_limit_is_accepted_and_one_level_more_is_refused()
    {
        Run(BuiltInTransforms.JsonMinify, Nested(BuiltInTransforms.JsonMaxDepth)).ShouldBe(Nested(BuiltInTransforms.JsonMaxDepth));
        ShouldFail(BuiltInTransforms.JsonMinify, Nested(BuiltInTransforms.JsonMaxDepth + 1));
        ShouldFail(BuiltInTransforms.JsonFormat, Nested(BuiltInTransforms.JsonMaxDepth + 1));
    }

    [Fact]
    public void A_hostile_deeply_nested_document_fails_without_crashing()
    {
        ShouldFail(BuiltInTransforms.JsonFormat, Nested(200_000));
    }

    [Fact]
    public void Strings_are_copied_exactly_as_written_so_escapes_are_neither_added_nor_removed()
    {
        // A lone surrogate escape is legal JSON and cannot be turned into text, so it is the proof.
        var json = @"{""a"":""\ud800 é \/ \b"",""b"":1}";

        Run(BuiltInTransforms.JsonMinify, json).ShouldBe(json);
        Run(BuiltInTransforms.JsonMinify, Run(BuiltInTransforms.JsonFormat, json)).ShouldBe(json);
    }

    [Fact]
    public void A_character_in_the_text_that_has_no_UTF_8_form_is_refused()
    {
        ShouldFail(BuiltInTransforms.JsonFormat, "{\"a\":\"x" + (char)0xD800 + "\"}");
    }

    [Fact]
    public void A_large_document_is_formatted_and_minified()
    {
        var items = string.Join(",", Enumerable.Range(0, 100_000).Select(i => "{\"id\":" + i + ",\"name\":\"item " + i + "\"}"));
        var json = "[" + items + "]";

        var formatted = Run(BuiltInTransforms.JsonFormat, json);

        formatted.Length.ShouldBeGreaterThan(json.Length);
        Run(BuiltInTransforms.JsonMinify, formatted).ShouldBe(json);
    }
}
