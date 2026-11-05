using ClipBench.Core.Detection;

namespace ClipBench.Core.Tests.Detection;

public class LinkEmailPhoneTests
{
    // --- links ---

    [Theory]
    [InlineData("https://learn.microsoft.com/windows/apps/winui/", "https://learn.microsoft.com/windows/apps/winui/")]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("HTTPS://Example.COM/Path?q=1&r=2#frag", "https://example.com/Path?q=1&r=2#frag")]
    [InlineData("  https://example.com/a  ", "https://example.com/a")]
    [InlineData("http://localhost:3000/app", "http://localhost:3000/app")]
    [InlineData("http://127.0.0.1:8080/", "http://127.0.0.1:8080/")]
    [InlineData("https://sub.domain.example.co.uk/x/y.html", "https://sub.domain.example.co.uk/x/y.html")]
    [InlineData("www.example.com", "https://www.example.com/")]
    [InlineData("www.example.com/some/page", "https://www.example.com/some/page")]
    public void A_web_address_is_a_link_and_the_address_to_open_is_written_out_in_full(string text, string address)
    {
        LinkDetector.TryParse(text).ShouldBe(address);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("\\\\server\\share")]
    [InlineData("https://user:pass@example.com/")]
    [InlineData("https://paypal.com@evil.example/")]
    [InlineData("http://")]
    [InlineData("https:///path")]
    [InlineData("https://exa mple.com")]
    [InlineData("see https://example.com for more")]
    [InlineData("https://example.com https://example.org")]
    [InlineData("https://example.com\nsecond line")]
    [InlineData("https://exam\tple.com")]
    [InlineData("https://example.com/\u0001")]
    [InlineData("www.")]
    [InlineData("www")]
    [InlineData("")]
    [InlineData("   ")]
    public void Anything_that_is_not_one_http_or_https_address_is_not_a_link(string text)
    {
        LinkDetector.TryParse(text).ShouldBeNull();
    }

    [Fact]
    public void An_address_that_is_far_too_long_is_not_a_link()
    {
        LinkDetector.TryParse("https://example.com/" + new string('a', 3000)).ShouldBeNull();
        LinkDetector.TryParse("https://example.com/" + new string('a', LinkDetector.MaxLength - 20)).ShouldNotBeNull();
    }

    [Fact]
    public void Missing_text_is_a_bug_for_a_link()
    {
        Should.Throw<ArgumentNullException>(() => LinkDetector.TryParse(null!));
    }

    // --- emails ---

    [Theory]
    [InlineData("someone@example.com")]
    [InlineData("first.last@example.co.uk")]
    [InlineData("first+tag@sub.example.org")]
    [InlineData("a_b-c@example-site.com")]
    [InlineData("  someone@example.com  ")]
    [InlineData("UPPER@EXAMPLE.COM")]
    [InlineData("1234@example.io")]
    public void A_single_email_address_is_one(string text)
    {
        EmailDetector.IsEmail(text).ShouldBeTrue();
    }

    [Theory]
    [InlineData("someone@")]
    [InlineData("@example.com")]
    [InlineData("someone@example")]
    [InlineData("someone@@example.com")]
    [InlineData("some one@example.com")]
    [InlineData("someone@exa mple.com")]
    [InlineData("someone@example..com")]
    [InlineData(".someone@example.com")]
    [InlineData("someone.@example.com")]
    [InlineData("some..one@example.com")]
    [InlineData("someone@-example.com")]
    [InlineData("someone@example-.com")]
    [InlineData("someone@example.c")]
    [InlineData("someone@example.123")]
    [InlineData("mail someone@example.com")]
    [InlineData("a@example.com, b@example.com")]
    [InlineData("<someone@example.com>")]
    [InlineData("someone@example.com\n")]
    [InlineData("so(me)one@example.com")]
    [InlineData("")]
    [InlineData("plain words")]
    public void Anything_that_is_not_exactly_one_email_address_is_not_one(string text)
    {
        // A trailing line break is trimmed like any other white space, so only that one case is let through.
        EmailDetector.IsEmail(text).ShouldBe(text == "someone@example.com\n");
    }

    [Fact]
    public void An_email_address_longer_than_the_limit_is_not_one()
    {
        EmailDetector.IsEmail(new string('a', 250) + "@example.com").ShouldBeFalse();
        EmailDetector.IsEmail(new string('a', 64) + "@example.com").ShouldBeTrue();
        EmailDetector.IsEmail(new string('a', 65) + "@example.com").ShouldBeFalse();
    }

    [Fact]
    public void Missing_text_is_a_bug_for_an_email()
    {
        Should.Throw<ArgumentNullException>(() => EmailDetector.IsEmail(null!));
    }

    // --- phone numbers ---

    [Theory]
    [InlineData("+1 (555) 123-4567")]
    [InlineData("(555) 123-4567")]
    [InlineData("555-123-4567")]
    [InlineData("555.123.4567")]
    [InlineData("+44 20 7946 0958")]
    [InlineData("+971 4 123 4567")]
    [InlineData("+966501234567")]
    [InlineData("0044 20 7946 0958")]
    [InlineData("  +1 555 123 4567  ")]
    [InlineData("123-4567")]
    public void A_phone_number_is_seven_to_fifteen_digits_with_the_usual_separators(string text)
    {
        PhoneDetector.IsPhone(text).ShouldBeTrue();
    }

    [Theory]
    [InlineData("5551234567")]
    [InlineData("123456")]
    [InlineData("1234567890123456")]
    [InlineData("+1234567890123456")]
    [InlineData("2026-11-05")]
    [InlineData("05.11.2026")]
    [InlineData("11-05-2026")]
    [InlineData("192.168.1.1")]
    [InlineData("10.0.0.255")]
    [InlineData("1.2.3")]
    [InlineData("12:30:45")]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("+")]
    [InlineData("++1 555 123 4567")]
    [InlineData("555-123-456a")]
    [InlineData("call 555-123-4567")]
    [InlineData("555-123-4567 or 555-987-6543")]
    [InlineData("(555 123-4567")]
    [InlineData("555) 123-4567")]
    [InlineData("--------")]
    [InlineData("")]
    [InlineData("words")]
    public void Anything_else_is_not_a_phone_number(string text)
    {
        PhoneDetector.IsPhone(text).ShouldBeFalse();
    }

    [Fact]
    public void Missing_text_is_a_bug_for_a_phone_number()
    {
        Should.Throw<ArgumentNullException>(() => PhoneDetector.IsPhone(null!));
    }
}
