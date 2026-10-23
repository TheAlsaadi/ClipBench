using ClipBench.Core.Results;
using ClipBench.Core.Transforms;

namespace ClipBench.Core.Tests.Transforms;

public class EncodingTransformsTests
{
    private const string Arabic = "\u0645\u0631\u062D\u0628\u0627";
    private const string Emoji = "\U0001F600";
    private const string LoneSurrogate = "a\uD800b";

    private static string Run(TextTransform transform, string text)
    {
        var result = transform.Apply(text);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private static void ShouldFail(TextTransform transform, string text)
    {
        var result = transform.Apply(text);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.TransformInputInvalid);
        result.Error.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("plain-text_ok.~", "plain-text_ok.~")]
    [InlineData("a b", "a%20b")]
    [InlineData("a&b=c/d?e#f", "a%26b%3Dc%2Fd%3Fe%23f")]
    [InlineData("100% sure", "100%25%20sure")]
    [InlineData("line\r\nbreak", "line%0D%0Abreak")]
    [InlineData("caf\u00E9", "caf%C3%A9")]
    [InlineData(Emoji, "%F0%9F%98%80")]
    [InlineData(Arabic, "%D9%85%D8%B1%D8%AD%D8%A8%D8%A7")]
    [InlineData("e\u0301", "e%CC%81")]
    [InlineData("+ stays plus", "%2B%20stays%20plus")]
    public void URL_encode_escapes_everything_but_the_unreserved_characters_as_UTF_8(string text, string expected)
    {
        Run(BuiltInTransforms.UrlEncode, text).ShouldBe(expected);
    }

    [Fact]
    public void URL_encode_refuses_a_lone_surrogate_instead_of_changing_it()
    {
        ShouldFail(BuiltInTransforms.UrlEncode, LoneSurrogate);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("a%20b", "a b")]
    [InlineData("a%26b%3Dc%2Fd", "a&b=c/d")]
    [InlineData("100%25%20sure", "100% sure")]
    [InlineData("caf%C3%A9", "caf\u00E9")]
    [InlineData("caf%c3%a9", "caf\u00E9")]
    [InlineData("%F0%9F%98%80", Emoji)]
    [InlineData("%D9%85%D8%B1%D8%AD%D8%A8%D8%A7", Arabic)]
    [InlineData("plain text stays", "plain text stays")]
    [InlineData("a+b", "a+b")]
    [InlineData("mixed %41 and caf\u00E9", "mixed A and caf\u00E9")]
    public void URL_decode_reads_percent_escapes_as_UTF_8_and_keeps_a_plus(string text, string expected)
    {
        Run(BuiltInTransforms.UrlDecode, text).ShouldBe(expected);
    }

    [Theory]
    [InlineData("100%")]
    [InlineData("%")]
    [InlineData("%2")]
    [InlineData("%zz")]
    [InlineData("a%2g")]
    [InlineData("%FF")]
    [InlineData("%C3")]
    [InlineData("%C3%28")]
    [InlineData("%ED%A0%80")]
    public void URL_decode_fails_on_a_broken_escape_or_bytes_that_are_not_UTF_8(string text)
    {
        ShouldFail(BuiltInTransforms.UrlDecode, text);
    }

    [Theory]
    [InlineData("a b&c=d/\u00E9 " + Emoji)]
    [InlineData("")]
    [InlineData(Arabic + " 100%")]
    public void URL_encode_then_decode_gives_back_the_text(string text)
    {
        Run(BuiltInTransforms.UrlDecode, Run(BuiltInTransforms.UrlEncode, text)).ShouldBe(text);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("hello", "aGVsbG8=")]
    [InlineData("hello!", "aGVsbG8h")]
    [InlineData("he", "aGU=")]
    [InlineData("caf\u00E9", "Y2Fmw6k=")]
    [InlineData(Emoji, "8J+YgA==")]
    [InlineData(Arabic, "2YXYsdit2KjYpw==")]
    [InlineData("line\r\nbreak", "bGluZQ0KYnJlYWs=")]
    public void Base64_encode_writes_the_UTF_8_bytes(string text, string expected)
    {
        Run(BuiltInTransforms.Base64Encode, text).ShouldBe(expected);
    }

    [Fact]
    public void Base64_encode_refuses_a_lone_surrogate_instead_of_changing_it()
    {
        ShouldFail(BuiltInTransforms.Base64Encode, LoneSurrogate);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("aGVsbG8=", "hello")]
    [InlineData("aGVsbG8", "hello")]
    [InlineData("  aGVsbG8=  ", "hello")]
    [InlineData("aGVs\r\nbG8=", "hello")]
    [InlineData("aGVs bG8=\t", "hello")]
    [InlineData("8J+YgA==", Emoji)]
    [InlineData("8J+YgA", Emoji)]
    [InlineData("2YXYsdit2KjYpw==", Arabic)]
    [InlineData("Y2Fmw6k=", "caf\u00E9")]
    public void Base64_decode_reads_standard_Base64_with_or_without_padding_and_white_space(string text, string expected)
    {
        Run(BuiltInTransforms.Base64Decode, text).ShouldBe(expected);
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("a")]
    [InlineData("aGVsbG8==")]
    [InlineData("aGVs=bG8")]
    [InlineData("aGVsbG8-")]
    [InlineData("/w==")]
    [InlineData("gA==")]
    [InlineData("\u00E9\u00E9\u00E9\u00E9")]
    public void Base64_decode_fails_on_text_that_is_not_Base64_or_does_not_decode_to_UTF_8_text(string text)
    {
        ShouldFail(BuiltInTransforms.Base64Decode, text);
    }

    [Fact]
    public void Base64_encode_then_decode_gives_back_the_text()
    {
        const string text = "caf\u00E9 \U0001F600 \u0645\u0631\u062D\u0628\u0627\r\nline two";

        Run(BuiltInTransforms.Base64Decode, Run(BuiltInTransforms.Base64Encode, text)).ShouldBe(text);
    }

    [Fact]
    public void Large_text_goes_through_all_four_transforms()
    {
        var text = string.Concat(Enumerable.Repeat("abc \u00E9 & ", 300_000));

        Run(BuiltInTransforms.UrlDecode, Run(BuiltInTransforms.UrlEncode, text)).ShouldBe(text);
        Run(BuiltInTransforms.Base64Decode, Run(BuiltInTransforms.Base64Encode, text)).ShouldBe(text);
    }
}
