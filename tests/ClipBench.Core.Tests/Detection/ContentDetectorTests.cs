using ClipBench.Core.Clips;
using ClipBench.Core.Detection;
using DetectionResult = ClipBench.Core.Detection.Detection;

namespace ClipBench.Core.Tests.Detection;

public class ContentDetectorTests
{
    [Fact]
    public void A_color_is_found_with_its_swatch()
    {
        var detection = ContentDetector.Detect("#1E90FF");

        detection.Kind.ShouldBe(DetectedKind.Color);
        detection.Color.ShouldBe(new ColorSwatch(0x1E, 0x90, 0xFF, 255));
        detection.Address.ShouldBeNull();
    }

    [Fact]
    public void A_link_is_found_with_the_address_to_open_and_nothing_is_fetched()
    {
        var detection = ContentDetector.Detect("https://learn.microsoft.com/windows/apps/winui/");

        detection.Kind.ShouldBe(DetectedKind.Link);
        detection.Address.ShouldBe("https://learn.microsoft.com/windows/apps/winui/");
    }

    [Fact]
    public void An_email_and_a_phone_number_are_found()
    {
        ContentDetector.Detect("someone@example.com").Kind.ShouldBe(DetectedKind.Email);
        ContentDetector.Detect("+1 (555) 123-4567").Kind.ShouldBe(DetectedKind.Phone);
    }

    [Fact]
    public void Code_is_found_with_a_guess_of_its_language()
    {
        var detection = ContentDetector.Detect("def f(x):\n    return x\n");

        detection.Kind.ShouldBe(DetectedKind.Code);
        detection.Language.ShouldBe("Python");
    }

    [Theory]
    [InlineData("just words")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026-11-05")]
    public void Ordinary_text_has_no_detection(string text)
    {
        ContentDetector.Detect(text).ShouldBe(DetectionResult.None);
        ContentDetector.Detect(text).Kind.ShouldBe(DetectedKind.None);
    }

    [Fact]
    public void A_color_wins_over_the_other_readings_of_the_same_text()
    {
        // "#fff" could be read as a hashtag and "rgb(1,2,3)" as a function call; the color is the useful reading.
        ContentDetector.Detect("#fff").Kind.ShouldBe(DetectedKind.Color);
        ContentDetector.Detect("rgb(1, 2, 3)").Kind.ShouldBe(DetectedKind.Color);
    }

    [Fact]
    public void A_clip_of_text_is_detected_from_its_text()
    {
        ContentDetector.Detect(new TextContent("#00ff00")).Kind.ShouldBe(DetectedKind.Color);
    }

    [Fact]
    public void Rich_text_and_html_are_detected_from_their_plain_text()
    {
        ContentDetector.Detect(new HtmlContent("<b>#00ff00</b>", "#00ff00")).Kind.ShouldBe(DetectedKind.Color);
    }

    [Fact]
    public void Pictures_and_file_lists_are_never_detected()
    {
        ContentDetector.Detect(new ImageContent(new byte[] { 1, 2, 3 })).ShouldBe(DetectionResult.None);
        ContentDetector.Detect(new FilesContent(["C:\\www.example.com"])).ShouldBe(DetectionResult.None);
    }

    [Fact]
    public void Missing_content_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => ContentDetector.Detect((string)null!));
        Should.Throw<ArgumentNullException>(() => ContentDetector.Detect((ClipContent)null!));
    }
}
