using System.Globalization;
using ClipBench.Core.Transforms;

namespace ClipBench.Core.Tests.Transforms;

public class TextTransformsTests
{
    private const string Arabic = "\u0645\u0631\u062D\u0628\u0627 \u0628\u0627\u0644\u0639\u0627\u0644\u0645";
    private const string Emoji = "\U0001F600";
    private const string CombiningAcute = "\u0301";

    private static string Run(TextTransform transform, string text)
    {
        var result = transform.Apply(text);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Hello, World 42!", "Hello, World 42!")]
    [InlineData("<b>bold</b> text", "<b>bold</b> text")]
    [InlineData("  keeps\r\nevery   thing\t", "  keeps\r\nevery   thing\t")]
    public void Plain_text_returns_the_text_unchanged(string text, string expected)
    {
        Run(BuiltInTransforms.PlainText, text).ShouldBe(expected);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("hello world", "HELLO WORLD")]
    [InlineData("Mixed 123 case!", "MIXED 123 CASE!")]
    [InlineData(Arabic, Arabic)]
    [InlineData(Emoji + " ok", Emoji + " OK")]
    [InlineData("e" + CombiningAcute + "cole", "E" + CombiningAcute + "COLE")]
    [InlineData("i", "I")]
    public void Upper_changes_every_letter(string text, string expected)
    {
        Run(BuiltInTransforms.Upper, text).ShouldBe(expected);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("HELLO World", "hello world")]
    [InlineData("A1 B2!", "a1 b2!")]
    [InlineData(Arabic, Arabic)]
    [InlineData(Emoji + " OK", Emoji + " ok")]
    [InlineData("E" + CombiningAcute + "COLE", "e" + CombiningAcute + "cole")]
    [InlineData("I", "i")]
    public void Lower_changes_every_letter(string text, string expected)
    {
        Run(BuiltInTransforms.Lower, text).ShouldBe(expected);
    }

    [Fact]
    public void Case_changes_do_not_depend_on_the_language_of_Windows()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            // Turkish would give a dotted capital for "i" and a dotless small letter for "I".
            "i".ToUpper(CultureInfo.CurrentCulture).ShouldBe("\u0130");
            Run(BuiltInTransforms.Upper, "title").ShouldBe("TITLE");
            Run(BuiltInTransforms.Lower, "TITLE").ShouldBe("title");
            Run(BuiltInTransforms.Title, "TITLE it").ShouldBe("Title It");
            Run(BuiltInTransforms.Sentence, "TITLE it. in time").ShouldBe("Title it. In time");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void The_Turkish_dotless_i_is_left_alone_by_upper_case_and_a_capital_I_lowers_to_a_plain_i()
    {
        // Documented in ADR 0016: the invariant culture knows nothing of the Turkish pairs.
        Run(BuiltInTransforms.Upper, "\u0131").ShouldBe("\u0131");
        Run(BuiltInTransforms.Lower, "I").ShouldBe("i");
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("hello world", "Hello World")]
    [InlineData("HELLO WORLD", "Hello World")]
    [InlineData("don't stop", "Don't Stop")]
    [InlineData("3rd place", "3rd Place")]
    [InlineData("hello-world", "Hello-World")]
    [InlineData("one\r\ntwo", "One\r\nTwo")]
    [InlineData("  spaced   out  ", "  Spaced   Out  ")]
    [InlineData(Arabic, Arabic)]
    [InlineData(Emoji + " smile", Emoji + " Smile")]
    [InlineData("e" + CombiningAcute + "cole normale", "E" + CombiningAcute + "cole Normale")]
    public void Title_capitalises_each_word_and_lowers_the_rest(string text, string expected)
    {
        Run(BuiltInTransforms.Title, text).ShouldBe(expected);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("hello world. how are you? fine!", "Hello world. How are you? Fine!")]
    [InlineData("HELLO THERE", "Hello there")]
    [InlineData("3.14 is pi. yes", "3.14 is pi. Yes")]
    [InlineData("one\ntwo", "One\nTwo")]
    [InlineData("  leading", "  Leading")]
    [InlineData("wait\u2026 what", "Wait\u2026 What")]
    [InlineData("he said. \"next one\"", "He said. \"Next one\"")]
    [InlineData("no end mark", "No end mark")]
    [InlineData("ends. ", "Ends. ")]
    [InlineData(Arabic + ". " + Arabic, Arabic + ". " + Arabic)]
    [InlineData("a.b c", "A.b c")]
    public void Sentence_capitalises_the_start_of_each_sentence_and_lowers_the_rest(string text, string expected)
    {
        Run(BuiltInTransforms.Sentence, text).ShouldBe(expected);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  a b \r\n", "a b")]
    [InlineData("\t\ttabbed\t", "tabbed")]
    [InlineData("\u00A0nbsp\u00A0", "nbsp")]
    [InlineData("inner  space  stays", "inner  space  stays")]
    [InlineData("no change", "no change")]
    [InlineData(Emoji + " ", Emoji)]
    public void Trim_removes_white_space_at_both_ends_only(string text, string expected)
    {
        Run(BuiltInTransforms.Trim, text).ShouldBe(expected);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("a\r\nb", "a b")]
    [InlineData("a\nb", "a b")]
    [InlineData("a\rb", "a b")]
    [InlineData("a\n\n\nb", "a b")]
    [InlineData("a \r\n b", "a b")]
    [InlineData("a\u2028b\u2029c\u0085d", "a b c d")]
    [InlineData("\r\na\r\n", "a")]
    [InlineData("\n\n", "")]
    [InlineData("  a\n b\n", "  a b")]
    [InlineData("no  breaks  here", "no  breaks  here")]
    [InlineData(Arabic + "\n" + Arabic, Arabic + " " + Arabic)]
    [InlineData(Emoji + "\n" + Emoji, Emoji + " " + Emoji)]
    public void Remove_line_breaks_joins_the_lines_with_one_space(string text, string expected)
    {
        Run(BuiltInTransforms.RemoveLineBreaks, text).ShouldBe(expected);
    }

    [Fact]
    public void Very_large_text_is_handled()
    {
        var text = string.Concat(Enumerable.Repeat("hello. wor ld\n", 400_000));

        Run(BuiltInTransforms.Upper, text).Length.ShouldBe(text.Length);
        Run(BuiltInTransforms.Title, text).Length.ShouldBe(text.Length);
        Run(BuiltInTransforms.Sentence, text).Length.ShouldBe(text.Length);
        Run(BuiltInTransforms.RemoveLineBreaks, text).Length.ShouldBe(text.Length - 1);
        Run(BuiltInTransforms.Trim, text).Length.ShouldBe(text.Length - 1);
    }

    [Fact]
    public void Every_transform_has_an_identifier_a_name_and_a_description()
    {
        TextTransform[] all =
        [
            BuiltInTransforms.PlainText, BuiltInTransforms.Upper, BuiltInTransforms.Lower, BuiltInTransforms.Title,
            BuiltInTransforms.Sentence, BuiltInTransforms.Trim, BuiltInTransforms.RemoveLineBreaks,
        ];

        all.Select(transform => transform.Id).Distinct().Count().ShouldBe(all.Length);
        all.ShouldAllBe(transform => transform.Name.Length > 0 && transform.Description.Length > 0);
    }
}
