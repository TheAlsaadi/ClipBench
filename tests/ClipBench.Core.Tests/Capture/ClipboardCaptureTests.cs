using ClipBench.Core.Capture;
using ClipBench.Core.Clips;

namespace ClipBench.Core.Tests.Capture;

public class ClipboardCaptureTests
{
    private static readonly TextContent _text = new("a secret text");

    [Fact]
    public void An_accepted_capture_carries_its_content_and_where_it_came_from()
    {
        var capture = ClipboardCapture.Accepted(42, _text, "keepass.exe");

        capture.Decision.IsAccepted.ShouldBeTrue();
        capture.SequenceNumber.ShouldBe(42);
        capture.Content.ShouldBe(_text);
        capture.SourceProcess.ShouldBe("keepass.exe");
    }

    [Fact]
    public void An_accepted_capture_may_have_an_unknown_source()
    {
        ClipboardCapture.Accepted(1, _text, null).SourceProcess.ShouldBeNull();
    }

    [Theory]
    [InlineData(CaptureRejection.Incognito)]
    [InlineData(CaptureRejection.OwnWrite)]
    [InlineData(CaptureRejection.PrivacyMarker)]
    [InlineData(CaptureRejection.ExcludedProcess)]
    [InlineData(CaptureRejection.TooLarge)]
    public void A_rejected_capture_has_a_reason_and_no_content_and_no_source(CaptureRejection reason)
    {
        var capture = ClipboardCapture.Rejected(7, reason);

        capture.Decision.IsAccepted.ShouldBeFalse();
        capture.Decision.Rejection.ShouldBe(reason);
        capture.SequenceNumber.ShouldBe(7);
        capture.Content.ShouldBeNull();
        capture.SourceProcess.ShouldBeNull();
    }

    [Fact]
    public void Rejecting_with_the_reason_none_is_a_bug()
    {
        Should.Throw<ArgumentException>(() => ClipboardCapture.Rejected(1, CaptureRejection.None));
    }

    [Fact]
    public void An_accepted_capture_without_content_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => ClipboardCapture.Accepted(1, null!, "a.exe"));
    }

    [Fact]
    public void Printing_a_capture_shows_neither_the_content_nor_the_source()
    {
        var accepted = ClipboardCapture.Accepted(3, _text, "keepass.exe").ToString();
        var rejected = ClipboardCapture.Rejected(4, CaptureRejection.PrivacyMarker).ToString();

        accepted.ShouldNotContain("secret");
        accepted.ShouldNotContain("keepass");
        accepted.ShouldContain("3");
        rejected.ShouldContain("PrivacyMarker");
    }
}
