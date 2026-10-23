using ClipBench.Core.Clips;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Popup;

/// <summary>A locked popup stays on screen, so the window it pastes into is chosen again for every paste.</summary>
public sealed class PasteFlowHoldTests : IDisposable
{
    private const int OwnProcess = 900;

    private static readonly WindowHandle Notepad = new(0x1234);
    private static readonly WindowHandle Browser = new(0x5678);
    private static readonly WindowHandle Popup = new(0x9ABC);

    private readonly FakeForegroundTracker _tracker = new();
    private readonly FakePasteInjector _injector = new();
    private readonly SettingsService _settings = new(new FakeSettingsStore());
    private readonly PasteFlow _flow;

    public PasteFlowHoldTests()
    {
        _flow = new PasteFlow(
            _tracker,
            _injector,
            new FakeClipboardWriter(),
            new FakeNotifier(),
            new FakeActivityLog(),
            _settings,
            new FakeQueueControl(),
            new PasteFlowOptions { OwnProcessId = OwnProcess });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _settings.Dispose();

    private static Clip Clip() => new(ClipId.New(), new TextContent("text"), DateTimeOffset.UnixEpoch);

    private static Result<ForegroundTarget> Window(WindowHandle window, int process) =>
        Result.Success(new ForegroundTarget(window, process, "app.exe", BlocksInputFromUs: false));

    [Fact]
    public async Task A_held_flow_keeps_its_window_for_the_next_paste()
    {
        _tracker.Next = Window(Notepad, 42);
        _flow.Begin();
        _flow.Hold(true);
        _tracker.Next = Window(Popup, OwnProcess);

        await _flow.PasteAsync(Clip(), Ct);
        await _flow.PasteAsync(Clip(), Ct);

        _injector.Requests.Select(request => request.Target).ShouldBe([Notepad, Notepad]);
    }

    [Fact]
    public async Task A_flow_that_is_not_held_forgets_its_window_after_one_paste()
    {
        _flow.Begin();

        await _flow.PasteAsync(Clip(), Ct);
        _tracker.Next = Window(Popup, OwnProcess);
        await _flow.PasteAsync(Clip(), Ct);

        _injector.Requests.Select(request => request.Target).ShouldBe([Notepad, null]);
    }

    [Fact]
    public async Task A_held_flow_pastes_into_the_program_in_front_now_when_it_is_not_ClipBench()
    {
        _tracker.Next = Window(Notepad, 42);
        _flow.Begin();
        _flow.Hold(true);

        // The user clicked into another program; the locked popup did not take the focus.
        _tracker.Next = Window(Browser, 43);
        await _flow.PasteAsync(Clip(), Ct);

        _injector.Requests.Single().Target.ShouldBe(Browser);
    }

    [Fact]
    public async Task The_program_pasted_into_last_stays_on_record_while_ClipBench_is_in_front()
    {
        _tracker.Next = Window(Notepad, 42);
        _flow.Begin();
        _flow.Hold(true);
        _tracker.Next = Window(Browser, 43);
        await _flow.PasteAsync(Clip(), Ct);

        _tracker.Next = Window(Popup, OwnProcess);
        await _flow.PasteAsync(Clip(), Ct);

        _injector.Requests.Select(request => request.Target).ShouldBe([Browser, Browser]);
    }

    [Fact]
    public async Task A_held_flow_with_nothing_in_front_uses_the_window_on_record()
    {
        _tracker.Next = Window(Notepad, 42);
        _flow.Begin();
        _flow.Hold(true);
        _tracker.Next = Result.Failure<ForegroundTarget>(new ResultError(ErrorCode.TargetNotFound, "no window"));

        await _flow.PasteAsync(Clip(), Ct);

        _injector.Requests.Single().Target.ShouldBe(Notepad);
    }

    [Fact]
    public async Task Letting_go_of_the_hold_goes_back_to_one_paste_per_window()
    {
        _tracker.Next = Window(Notepad, 42);
        _flow.Begin();
        _flow.Hold(true);
        await _flow.PasteAsync(Clip(), Ct);

        _flow.Hold(false);
        await _flow.PasteAsync(Clip(), Ct);
        _tracker.Next = Window(Popup, OwnProcess);
        await _flow.PasteAsync(Clip(), Ct);

        _injector.Requests.Select(request => request.Target).ShouldBe([Notepad, Notepad, null]);
    }

    [Fact]
    public async Task Composed_content_is_also_pasted_into_the_program_in_front_now()
    {
        _tracker.Next = Window(Notepad, 42);
        _flow.Begin();
        _flow.Hold(true);
        _tracker.Next = Window(Browser, 43);

        await _flow.PasteContentAsync(new TextContent("a, b"), Ct);

        _injector.Requests.Single().Target.ShouldBe(Browser);
    }

    [Fact]
    public void Returning_gives_the_focus_back_to_the_window_on_record()
    {
        _tracker.Next = Window(Notepad, 42);
        _flow.Begin();

        _flow.ReturnToTarget();

        _tracker.Restored.ShouldBe([Notepad]);
    }

    [Fact]
    public void Returning_with_no_window_on_record_does_nothing()
    {
        _flow.Begin(recordTarget: false);

        _flow.ReturnToTarget();

        _tracker.Restored.ShouldBeEmpty();
    }
}
