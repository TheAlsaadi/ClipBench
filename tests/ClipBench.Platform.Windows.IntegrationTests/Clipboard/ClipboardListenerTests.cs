using System.Collections.Concurrent;
using ClipBench.Core.Capture;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.IntegrationTests.Support;

namespace ClipBench.Platform.Windows.IntegrationTests.Clipboard;

public sealed class ClipboardListenerTests : IDisposable
{
    private const uint ClipboardUpdate = 0x031D;   // WM_CLIPBOARDUPDATE

    private readonly FakeWindow _window = new();
    private readonly FakeNotifications _notifications;
    private readonly FakeClipboardApi _api = new();
    private readonly ClipboardListener _listener;
    private readonly BlockingCollection<(long Sequence, int Thread)> _events = [];

    public ClipboardListenerTests()
    {
        _notifications = new FakeNotifications(_window);
        _listener = new ClipboardListener(_window, _window, _notifications, _api);
        _listener.Changed += (_, args) => _events.Add((args.SequenceNumber, Environment.CurrentManagedThreadId));
    }

    public void Dispose()
    {
        _listener.Dispose();
        _events.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task StartAsync() => (await _listener.StartAsync(Ct)).IsSuccess.ShouldBeTrue();

    // Another program copies something: it empties the clipboard and sets its content, so a new sequence number
    // and no leftovers from before. Then Windows sends the message.
    private long Change(string text = "x")
    {
        _api.Clear();
        _api.PutText(text);
        _window.Send(ClipboardUpdate).ShouldBeTrue();
        return _api.SequenceNumber;
    }

    private (long Sequence, int Thread) Next() =>
        _events.TryTake(out var item, 10_000, Ct) ? item : throw new TimeoutException("no event arrived");

    private bool NothingMoreArrives() => !_events.TryTake(out _, 150, Ct);

    // --- starting ---

    [Fact]
    public async Task Starting_asks_Windows_for_notifications_once_on_the_window_thread()
    {
        await StartAsync();
        await StartAsync();

        _notifications.Subscriptions.ShouldBe(1);
    }

    [Fact]
    public async Task If_Windows_refuses_the_notifications_starting_fails_and_can_be_tried_again()
    {
        _notifications.Accepts = false;

        var failed = await _listener.StartAsync(Ct);

        failed.IsFailure.ShouldBeTrue();
        failed.Error.Code.ShouldBe(ErrorCode.PlatformFailure);

        _notifications.Accepts = true;
        await StartAsync();
        _notifications.Subscriptions.ShouldBe(2);
    }

    [Fact]
    public void Changes_before_the_listener_is_started_are_not_reported()
    {
        Change();

        NothingMoreArrives().ShouldBeTrue();
    }

    // --- reporting changes ---

    [Fact]
    public async Task A_change_is_reported_once_with_its_sequence_number_and_off_the_window_thread()
    {
        await StartAsync();

        var sequence = Change("copied");

        var (reported, thread) = Next();
        reported.ShouldBe(sequence);
        thread.ShouldNotBe(Environment.CurrentManagedThreadId);
        NothingMoreArrives().ShouldBeTrue();
    }

    [Fact]
    public async Task A_message_Windows_sends_twice_for_one_change_is_reported_once()
    {
        await StartAsync();
        Change();

        _window.Send(ClipboardUpdate);
        _window.Send(ClipboardUpdate);

        Next();
        NothingMoreArrives().ShouldBeTrue();
    }

    [Fact]
    public async Task Each_new_change_is_reported_in_order()
    {
        await StartAsync();

        var first = Change("one");
        var second = Change("two");
        var third = Change("three");

        Next().Sequence.ShouldBe(first);
        Next().Sequence.ShouldBe(second);
        Next().Sequence.ShouldBe(third);
    }

    [Fact]
    public async Task The_first_message_after_starting_reports_what_is_already_on_the_clipboard()
    {
        // Windows sends one message when a window starts listening. The app decides whether to keep what was already there.
        _api.PutText("copied before ClipBench started");
        await StartAsync();

        _window.Send(ClipboardUpdate);

        Next().Sequence.ShouldBe(_api.SequenceNumber);
    }

    [Fact]
    public async Task ClipBenchs_own_writes_are_not_reported_but_the_next_real_change_is()
    {
        await StartAsync();
        _api.PutText("our own paste");
        _api.PutMarker(ClipboardFormatNames.OwnWrite);
        _window.Send(ClipboardUpdate);
        NothingMoreArrives().ShouldBeTrue();

        var real = Change("a real copy");

        Next().Sequence.ShouldBe(real);
    }

    [Fact]
    public async Task A_change_by_another_program_right_after_our_write_is_reported_because_the_marker_is_gone()
    {
        await StartAsync();
        _api.PutText("our write");
        _api.PutMarker(ClipboardFormatNames.OwnWrite);

        // Before the message for our write is handled, another program replaces the content.
        _api.Clear();
        _api.PutText("theirs");
        _window.Send(ClipboardUpdate);

        Next().Sequence.ShouldBe(_api.SequenceNumber);
    }

    [Fact]
    public async Task A_subscriber_that_throws_does_not_stop_later_events()
    {
        await StartAsync();
        var second = new ConcurrentQueue<long>();
        _listener.Changed += (_, _) => throw new InvalidOperationException("a subscriber bug");
        _listener.Changed += (_, args) => second.Enqueue(args.SequenceNumber);

        Change();
        var last = Change();

        Next();
        Next();
        SpinWait.SpinUntil(() => second.Contains(last), TimeSpan.FromSeconds(10)).ShouldBeTrue();
    }

    // --- stopping ---

    [Fact]
    public async Task Disposing_stops_the_notifications_and_ignores_later_messages()
    {
        await StartAsync();

        _listener.Dispose();
        Change();

        _notifications.Unsubscriptions.ShouldBe(1);
        NothingMoreArrives().ShouldBeTrue();
    }

    [Fact]
    public void Disposing_a_listener_that_never_started_does_not_ask_Windows_to_stop()
    {
        _listener.Dispose();
        _listener.Dispose();

        _notifications.Unsubscriptions.ShouldBe(0);
    }

    [Fact]
    public async Task Starting_after_dispose_is_a_bug()
    {
        _listener.Dispose();

        await Should.ThrowAsync<ObjectDisposedException>(_listener.StartAsync(Ct));
    }
}
