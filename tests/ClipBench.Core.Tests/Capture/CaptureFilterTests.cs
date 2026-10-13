using System.Collections.Immutable;
using ClipBench.Core.Capture;
using ClipBench.Core.Clips;

namespace ClipBench.Core.Tests.Capture;

public class CaptureFilterTests
{
    private const long MiB = 1024 * 1024;

    private static CaptureRequest Request(
        ClipFormat format = ClipFormat.Text,
        long size = 100,
        string[]? formats = null,
        string? process = "notepad.exe",
        int? canInclude = null) =>
        new()
        {
            Format = format,
            SizeInBytes = size,
            FormatNames = [.. formats ?? ["CF_UNICODETEXT"]],
            SourceProcess = process,
            CanIncludeInClipboardHistory = canInclude,
        };

    private static CaptureFilter Filter(params string[] excluded) => new() { ExcludedProcesses = [.. excluded] };

    // --- normal content ---

    [Fact]
    public void Normal_content_is_accepted()
    {
        var decision = new CaptureFilter().Evaluate(Request());

        decision.IsAccepted.ShouldBeTrue();
        decision.Rejection.ShouldBe(CaptureRejection.None);
    }

    [Fact]
    public void A_request_with_no_format_list_and_no_source_is_accepted()
    {
        var request = new CaptureRequest { Format = ClipFormat.Text, SizeInBytes = 5 };

        new CaptureFilter().Evaluate(request).IsAccepted.ShouldBeTrue();
        new CaptureFilter().Evaluate(request with { FormatNames = default }).IsAccepted.ShouldBeTrue();
    }

    // --- privacy markers ---

    [Theory]
    [InlineData("ExcludeClipboardContentFromMonitorProcessing")]
    [InlineData("Clipboard Viewer Ignore")]
    public void Content_that_carries_a_privacy_marker_is_never_captured(string marker)
    {
        var decision = new CaptureFilter().Evaluate(Request(formats: ["CF_UNICODETEXT", marker]));

        decision.IsAccepted.ShouldBeFalse();
        decision.Rejection.ShouldBe(CaptureRejection.PrivacyMarker);
    }

    [Theory]
    [InlineData("excludeclipboardcontentfrommonitorprocessing")]
    [InlineData("CLIPBOARD VIEWER IGNORE")]
    public void Marker_names_are_matched_without_regard_to_case(string marker)
    {
        new CaptureFilter().Evaluate(Request(formats: [marker])).Rejection.ShouldBe(CaptureRejection.PrivacyMarker);
    }

    [Fact]
    public void The_history_flag_set_to_zero_means_do_not_record()
    {
        var request = Request(formats: ["CF_UNICODETEXT", "CanIncludeInClipboardHistory"], canInclude: 0);

        new CaptureFilter().Evaluate(request).Rejection.ShouldBe(CaptureRejection.PrivacyMarker);
    }

    [Fact]
    public void A_zero_history_flag_is_honoured_even_if_the_format_list_does_not_mention_it()
    {
        new CaptureFilter().Evaluate(Request(canInclude: 0)).Rejection.ShouldBe(CaptureRejection.PrivacyMarker);
    }

    [Fact]
    public void The_history_flag_set_to_one_allows_recording()
    {
        var request = Request(formats: ["CF_UNICODETEXT", "CanIncludeInClipboardHistory"], canInclude: 1);

        new CaptureFilter().Evaluate(request).IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public void A_history_flag_whose_value_could_not_be_read_is_treated_as_do_not_record()
    {
        // Fail closed: when we cannot tell whether the app asked for privacy, we assume it did.
        var request = Request(formats: ["CF_UNICODETEXT", "CanIncludeInClipboardHistory"], canInclude: null);

        new CaptureFilter().Evaluate(request).Rejection.ShouldBe(CaptureRejection.PrivacyMarker);
    }

    // --- excluded applications ---

    [Theory]
    [InlineData("KeePass.exe", "KeePass.exe")]
    [InlineData("KeePass.exe", "keepass.exe")]
    [InlineData("keepass.exe", "KEEPASS.EXE")]
    [InlineData("KeePass", "keepass.exe")]
    [InlineData("keepass.exe", "KeePass")]
    [InlineData("KeePass.exe", @"C:\Program Files\KeePass Password Safe 2\KeePass.exe")]
    [InlineData(@"C:\Tools\keepass.exe", "KeePass.exe")]
    [InlineData("KeePass.exe", "C:/Program Files/KeePass/keepass.EXE")]
    [InlineData("  KeePass.exe  ", "KeePass.exe")]
    public void An_excluded_application_is_never_captured_whatever_the_case_or_form_of_its_name(string excluded, string process)
    {
        var decision = Filter(excluded).Evaluate(Request(process: process));

        decision.Rejection.ShouldBe(CaptureRejection.ExcludedProcess);
    }

    [Theory]
    [InlineData("keepassxc.exe")]
    [InlineData("notkeepass.exe")]
    [InlineData("keep.exe")]
    [InlineData("keepass.exe.bak")]
    public void Only_the_whole_executable_name_matches(string process)
    {
        Filter("keepass.exe").Evaluate(Request(process: process)).IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public void Other_applications_are_captured_when_some_are_excluded()
    {
        Filter("keepass.exe", "bitwarden.exe").Evaluate(Request(process: "chrome.exe")).IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public void An_unknown_source_is_captured()
    {
        Filter("keepass.exe").Evaluate(Request(process: null)).IsAccepted.ShouldBeTrue();
        Filter("keepass.exe").Evaluate(Request(process: "")).IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public void A_blank_entry_in_the_exclusion_list_matches_nothing()
    {
        Filter("", "   ", ".exe").Evaluate(Request(process: "notepad.exe")).IsAccepted.ShouldBeTrue();
        Filter("", "   ").Evaluate(Request(process: null)).IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public void An_exclusion_list_that_was_never_set_excludes_nothing()
    {
        (new CaptureFilter { ExcludedProcesses = default }).Evaluate(Request()).IsAccepted.ShouldBeTrue();
    }

    // --- incognito ---

    [Fact]
    public void Nothing_is_captured_in_incognito()
    {
        var incognito = new CaptureFilter { Incognito = true };

        incognito.Evaluate(Request()).Rejection.ShouldBe(CaptureRejection.Incognito);
    }

    [Fact]
    public void Turning_incognito_on_gives_a_new_filter_and_leaves_the_old_one_alone()
    {
        var normal = new CaptureFilter();

        var incognito = normal with { Incognito = true };

        normal.Evaluate(Request()).IsAccepted.ShouldBeTrue();
        incognito.Evaluate(Request()).IsAccepted.ShouldBeFalse();
        (incognito with { Incognito = false }).Evaluate(Request()).IsAccepted.ShouldBeTrue();
    }

    // --- own writes ---

    [Theory]
    [InlineData("ClipBench.Internal")]
    [InlineData("clipbench.internal")]
    public void ClipBench_own_writes_are_ignored(string marker)
    {
        var request = Request(formats: ["CF_UNICODETEXT", marker]);

        request.IsOwnWrite.ShouldBeTrue();
        new CaptureFilter().Evaluate(request).Rejection.ShouldBe(CaptureRejection.OwnWrite);
    }

    [Fact]
    public void Ordinary_content_is_not_an_own_write()
    {
        Request().IsOwnWrite.ShouldBeFalse();
        new CaptureRequest { Format = ClipFormat.Text, SizeInBytes = 1, FormatNames = default }.IsOwnWrite.ShouldBeFalse();
    }

    // --- size limits ---

    [Fact]
    public void The_default_limits_are_two_megabytes_of_text_and_twenty_of_image()
    {
        CaptureLimits.Default.MaxTextBytes.ShouldBe(2 * MiB);
        CaptureLimits.Default.MaxImageBytes.ShouldBe(20 * MiB);
        new CaptureFilter().Limits.ShouldBe(CaptureLimits.Default);
    }

    [Theory]
    [InlineData(ClipFormat.Text)]
    [InlineData(ClipFormat.Html)]
    [InlineData(ClipFormat.Rtf)]
    [InlineData(ClipFormat.Files)]
    public void Text_like_content_may_be_exactly_the_text_limit_but_not_a_byte_more(ClipFormat format)
    {
        var filter = new CaptureFilter();

        filter.Evaluate(Request(format, size: 2 * MiB)).IsAccepted.ShouldBeTrue();
        filter.Evaluate(Request(format, size: (2 * MiB) + 1)).Rejection.ShouldBe(CaptureRejection.TooLarge);
    }

    [Fact]
    public void An_image_may_be_exactly_the_image_limit_but_not_a_byte_more()
    {
        var filter = new CaptureFilter();

        filter.Evaluate(Request(ClipFormat.Image, size: 20 * MiB)).IsAccepted.ShouldBeTrue();
        filter.Evaluate(Request(ClipFormat.Image, size: (20 * MiB) + 1)).Rejection.ShouldBe(CaptureRejection.TooLarge);
    }

    [Fact]
    public void An_image_may_be_larger_than_the_text_limit()
    {
        new CaptureFilter().Evaluate(Request(ClipFormat.Image, size: 5 * MiB)).IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public void The_limits_can_be_changed()
    {
        var filter = new CaptureFilter { Limits = new CaptureLimits { MaxTextBytes = 10, MaxImageBytes = 50 } };

        filter.Evaluate(Request(size: 10)).IsAccepted.ShouldBeTrue();
        filter.Evaluate(Request(size: 11)).Rejection.ShouldBe(CaptureRejection.TooLarge);
        filter.Evaluate(Request(ClipFormat.Image, size: 50)).IsAccepted.ShouldBeTrue();
        filter.Evaluate(Request(ClipFormat.Image, size: 51)).Rejection.ShouldBe(CaptureRejection.TooLarge);
    }

    [Fact]
    public void Empty_content_is_within_the_limits()
    {
        new CaptureFilter().Evaluate(Request(size: 0)).IsAccepted.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_limit_that_is_not_positive_is_a_bug(long limit)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new CaptureLimits { MaxTextBytes = limit });
        Should.Throw<ArgumentOutOfRangeException>(() => new CaptureLimits { MaxImageBytes = limit });
    }

    [Fact]
    public void A_negative_size_is_a_bug()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Request(size: -1));
    }

    [Fact]
    public void An_unknown_format_is_a_bug()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new CaptureFilter().Evaluate(Request((ClipFormat)99)));
    }

    // --- order of reasons ---

    [Fact]
    public void Incognito_is_reported_before_everything_else()
    {
        var request = Request(ClipFormat.Text, size: 3 * MiB, formats: ["Clipboard Viewer Ignore", "ClipBench.Internal"], process: "keepass.exe");
        var filter = new CaptureFilter { Incognito = true, ExcludedProcesses = ["keepass.exe"] };

        filter.Evaluate(request).Rejection.ShouldBe(CaptureRejection.Incognito);
    }

    [Fact]
    public void A_privacy_marker_is_reported_before_the_application_and_the_size()
    {
        var request = Request(ClipFormat.Text, size: 3 * MiB, formats: ["Clipboard Viewer Ignore"], process: "keepass.exe");

        Filter("keepass.exe").Evaluate(request).Rejection.ShouldBe(CaptureRejection.PrivacyMarker);
    }

    [Fact]
    public void An_own_write_is_reported_before_a_privacy_marker()
    {
        var request = Request(formats: ["Clipboard Viewer Ignore", "ClipBench.Internal"]);

        new CaptureFilter().Evaluate(request).Rejection.ShouldBe(CaptureRejection.OwnWrite);
    }

    [Fact]
    public void An_excluded_application_is_reported_before_the_size()
    {
        Filter("keepass.exe").Evaluate(Request(size: 3 * MiB, process: "keepass.exe")).Rejection
            .ShouldBe(CaptureRejection.ExcludedProcess);
    }

    [Fact]
    public void A_missing_request_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new CaptureFilter().Evaluate(null!));
    }

    [Fact]
    public void The_exclusion_list_is_not_changed_by_evaluating()
    {
        ImmutableArray<string> list = ["keepass.exe"];
        var filter = new CaptureFilter { ExcludedProcesses = list };

        filter.Evaluate(Request(process: "KeePass.exe"));

        filter.ExcludedProcesses.ShouldBe(list);
    }
}
