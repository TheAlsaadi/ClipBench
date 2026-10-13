using System.Diagnostics;
using ClipBench.Platform.Windows.Formats;

namespace ClipBench.Platform.Windows.IntegrationTests.Formats;

public class HtmlTextTests
{
    [Theory]
    [InlineData("<b>Hello</b> world", "Hello world")]
    [InlineData("no tags at all", "no tags at all")]
    [InlineData("", "")]
    [InlineData("1 > 0", "1 > 0")]
    [InlineData("a < b", "a < b")]
    [InlineData("<3 you", "<3 you")]
    public void Tags_are_removed_and_the_text_between_them_is_kept(string html, string expected)
    {
        HtmlText.Strip(html).ShouldBe(expected);
    }

    [Theory]
    [InlineData("<p>One</p><p>Two</p>", "One\nTwo")]
    [InlineData("One<br>Two<br/>Three<BR />", "One\nTwo\nThree")]
    [InlineData("<div>a</div><div>b</div>", "a\nb")]
    [InlineData("<ul><li>x</li><li>y</li></ul>", "x\ny")]
    [InlineData("<h1>Title</h1>Body", "Title\nBody")]
    [InlineData("<tr><td>1</td><td>2</td></tr><tr><td>3</td></tr>", "12\n3")]
    [InlineData("<br/>after", "after")]
    [InlineData("<p></p><p></p>text", "text")]
    public void Block_elements_and_line_breaks_become_one_line_break(string html, string expected)
    {
        HtmlText.Strip(html).ShouldBe(expected);
    }

    [Theory]
    [InlineData("a &amp; b", "a & b")]
    [InlineData("&lt;tag&gt;", "<tag>")]
    [InlineData("&quot;q&quot; &apos;s&apos;", "\"q\" 's'")]
    [InlineData("&#65;&#x42;&#X43;", "ABC")]
    [InlineData("&#128512;", "😀")]
    [InlineData("a&nbsp;b", "a b")]
    public void The_common_entities_and_numeric_references_are_decoded(string html, string expected)
    {
        HtmlText.Strip(html).ShouldBe(expected);
    }

    [Theory]
    [InlineData("&unknown;")]
    [InlineData("&")]
    [InlineData("&amp")]
    [InlineData("&#;")]
    [InlineData("&#x;")]
    [InlineData("&#0;")]
    [InlineData("&;")]
    [InlineData("&#")]
    [InlineData("&#xD800;")]
    [InlineData("&#x110000;")]
    [InlineData("&#99999999999999999999;")]
    public void An_entity_that_is_not_valid_is_left_as_it_was_written(string html)
    {
        HtmlText.Strip(html).ShouldBe(html);
    }

    [Fact]
    public void An_unfinished_tag_drops_the_rest_instead_of_showing_markup()
    {
        HtmlText.Strip("before <div class=\"x").ShouldBe("before ");
    }

    [Fact]
    public void A_comment_is_dropped_even_when_it_contains_a_closing_bracket()
    {
        HtmlText.Strip("a<!-- 1 > 0 -->b").ShouldBe("ab");
        HtmlText.Strip("a<!-- never closed").ShouldBe("a");
    }

    [Fact]
    public void Hostile_input_takes_linear_time()
    {
        var stopwatch = Stopwatch.StartNew();

        HtmlText.Strip(new string('<', 2_000_000));
        HtmlText.Strip(new string('&', 2_000_000));
        HtmlText.Strip(string.Concat(Enumerable.Repeat("&#", 1_000_000)));
        HtmlText.Strip("<!--" + new string('-', 2_000_000));

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_lone_surrogate_in_the_input_is_kept_out_of_the_output()
    {
        HtmlText.Strip("a\uD800b").ShouldBe("a\uFFFDb");
    }
}
