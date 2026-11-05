using System.Collections.Immutable;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;
using ClipBench.Platform.Windows.Screen;

namespace ClipBench.Platform.Windows.IntegrationTests.Screen;

public class SelectionOverlayTests
{
    private sealed class FakeLayout(ImmutableArray<MonitorBounds> monitors) : IMonitorLayout
    {
        public ImmutableArray<MonitorBounds> GetMonitors() => monitors;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_no_monitor_to_show_on_the_selection_fails_with_a_message_and_shows_nothing()
    {
        var overlay = new SelectionOverlay(new FakeLayout([]));

        var result = await overlay.PickAsync(Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.ScreenCaptureFailed);
        result.Error.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_selection_asked_for_with_a_cancelled_token_throws_before_showing_anything()
    {
        var overlay = new SelectionOverlay(new FakeLayout([new MonitorBounds(new PixelRect(0, 0, 800, 600), 1.0)]));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => overlay.PickAsync(source.Token));
    }

    [Fact]
    public void A_missing_layout_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new SelectionOverlay(null!));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancelling_while_the_overlay_is_shown_closes_it_and_reports_a_cancel_on_the_real_monitors()
    {
        var overlay = new SelectionOverlay(new MonitorLayout(new Win32ScreenApi()));
        using var source = new CancellationTokenSource();

        var pick = overlay.PickAsync(source.Token);
        await Task.Delay(500, Ct);
        pick.IsCompleted.ShouldBeFalse("the overlay should stay until the user chooses or cancels");
        await source.CancelAsync();
        var result = await pick.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.SelectionCancelled);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_second_selection_while_one_is_open_is_refused_and_a_new_one_can_start_after_the_first_ends()
    {
        var overlay = new SelectionOverlay(new MonitorLayout(new Win32ScreenApi()));
        using var source = new CancellationTokenSource();
        var first = overlay.PickAsync(source.Token);
        await Task.Delay(300, Ct);

        var second = await overlay.PickAsync(Ct);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe(ErrorCode.ScreenTextBusy);
        await source.CancelAsync();
        await first.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        using var again = new CancellationTokenSource();
        var third = overlay.PickAsync(again.Token);
        await Task.Delay(300, Ct);
        await again.CancelAsync();
        (await third.WaitAsync(TimeSpan.FromSeconds(10), Ct)).Error.Code.ShouldBe(ErrorCode.SelectionCancelled);
    }
}
