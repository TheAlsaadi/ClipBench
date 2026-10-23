using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.IntegrationTests.Support;
using ClipBench.Platform.Windows.Screen;

namespace ClipBench.Platform.Windows.IntegrationTests.Screen;

/// <summary>Applies the effects to a real window of the test program and asks Windows what it kept.</summary>
[Trait("Category", "Integration")]
public class RealWindowEffectsTests
{
    private readonly Win32WindowEffects _effects = new();

    [Fact]
    public void A_window_starts_out_visible_to_screenshots()
    {
        using var form = TestEditForm.Create();

        _effects.IsExcludedFromCapture(new WindowHandle(form.Handle)).ShouldBeFalse();
    }

    [Fact]
    public void Excluding_a_window_from_capture_is_kept_by_windows()
    {
        using var form = TestEditForm.Create();
        var window = new WindowHandle(form.Handle);

        var result = _effects.ExcludeFromCapture(window);

        result.IsSuccess.ShouldBeTrue();
        _effects.IsExcludedFromCapture(window).ShouldBeTrue();
    }

    [Fact]
    public void Excluding_twice_is_fine()
    {
        using var form = TestEditForm.Create();
        var window = new WindowHandle(form.Handle);

        _effects.ExcludeFromCapture(window).IsSuccess.ShouldBeTrue();

        _effects.ExcludeFromCapture(window).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_window_that_does_not_exist_cannot_be_excluded()
    {
        var result = _effects.ExcludeFromCapture(new WindowHandle(0x7FFF_FFF0));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.PlatformFailure);
        _effects.IsExcludedFromCapture(new WindowHandle(0x7FFF_FFF0)).ShouldBeFalse();
        _effects.ExcludeFromCapture(WindowHandle.None).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Rounded_corners_are_accepted_on_a_real_window_and_refused_for_a_missing_one()
    {
        using var form = TestEditForm.Create();

        // Windows 10 has no rounded corners and says so: the call is allowed to fail there, and never throws.
        var onWindows11 = Environment.OSVersion.Version.Build >= 22000;
        var result = _effects.UseRoundedCorners(new WindowHandle(form.Handle));
        if (onWindows11)
        {
            result.IsSuccess.ShouldBeTrue();
        }

        _effects.UseRoundedCorners(new WindowHandle(0x7FFF_FFF0)).IsFailure.ShouldBeTrue();
        _effects.UseRoundedCorners(WindowHandle.None).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void The_system_border_can_be_turned_off_on_a_real_window_and_is_refused_for_a_missing_one()
    {
        using var form = TestEditForm.Create();

        // Windows 10 has no window border colour and says so: the call is allowed to fail there, and never throws.
        var onWindows11 = Environment.OSVersion.Version.Build >= 22000;
        var result = _effects.HideSystemBorder(new WindowHandle(form.Handle));
        if (onWindows11)
        {
            result.IsSuccess.ShouldBeTrue();
        }

        _effects.HideSystemBorder(new WindowHandle(0x7FFF_FFF0)).IsFailure.ShouldBeTrue();
        _effects.HideSystemBorder(WindowHandle.None).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Removing_the_frame_makes_the_client_area_fill_the_whole_window()
    {
        using var form = TestEditForm.Create();
        form.ClientFillsWindow.ShouldBeFalse("a window starts out with a frame");

        var result = form.OnWindowThread(() => _effects.RemoveFrame(new WindowHandle(form.Handle)));

        result.IsSuccess.ShouldBeTrue();
        form.ClientFillsWindow.ShouldBeTrue();

        // Asking again changes nothing and is not an error.
        form.OnWindowThread(() => _effects.RemoveFrame(new WindowHandle(form.Handle))).IsSuccess.ShouldBeTrue();
        form.ClientFillsWindow.ShouldBeTrue();
    }

    [Fact]
    public void The_frame_of_a_missing_window_cannot_be_removed()
    {
        _effects.RemoveFrame(new WindowHandle(0x7FFF_FFF0)).IsFailure.ShouldBeTrue();
        _effects.RemoveFrame(WindowHandle.None).IsFailure.ShouldBeTrue();
    }
}
