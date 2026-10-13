using System.Collections.Concurrent;
using System.Text;
using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.IntegrationTests.Support;

namespace ClipBench.Platform.Windows.IntegrationTests.Clipboard;

/// <summary>
/// The clipboard listener on the real operating system. Like the other real-clipboard tests these replace the
/// clipboard while they run, and the fixture puts the user's clipboard back afterwards.
/// </summary>
[Collection("Clipboard")]
[Trait("Category", "Integration")]
public sealed class RealListenerTests(RealClipboardFixture clipboard) : IDisposable
{
    private readonly ClipboardListener _listener = new(
        clipboard.Window,
        clipboard.Window,
        new Win32ClipboardNotifications(clipboard.Window.Handle),
        clipboard.Api);

    private readonly BlockingCollection<long> _events = [];
    private bool _subscribed;

    public void Dispose()
    {
        _listener.Dispose();
        _events.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task StartAsync()
    {
        if (!_subscribed)
        {
            _listener.Changed += (_, args) => _events.Add(args.SequenceNumber);
            _subscribed = true;
        }

        (await _listener.StartAsync(Ct)).IsSuccess.ShouldBeTrue();

        // Windows announces the content that is already there when a window starts listening. Let that settle.
        while (_events.TryTake(out _, 500, Ct))
        {
        }
    }

    private async Task PutAsync(string text)
    {
        await TestClipboard.UseAsync(
            clipboard.ForeignApi,
            session =>
            {
                session.Empty().ShouldBeTrue();
                session.Set(ClipboardFormats.UnicodeText, Encoding.Unicode.GetBytes(text + "\0")).ShouldBeTrue();
                return 0;
            },
            Ct);
    }

    private long NextEvent() => _events.TryTake(out var sequence, 10_000, Ct) ? sequence : throw new TimeoutException("no clipboard change was reported");

    private bool NothingMore() => !_events.TryTake(out _, 600, Ct);

    [Fact]
    public async Task A_copy_by_another_program_is_reported_once_with_the_sequence_number_of_that_copy()
    {
        await StartAsync();

        await PutAsync("first copy");

        NextEvent().ShouldBe(clipboard.Api.SequenceNumber);
        NothingMore().ShouldBeTrue("Windows may send more than one message for a change; the listener reports one");
    }

    [Fact]
    public async Task Each_new_copy_is_reported_in_order()
    {
        await StartAsync();

        await PutAsync("one");
        var first = NextEvent();
        await PutAsync("two");
        var second = NextEvent();

        second.ShouldBeGreaterThan(first);
        NothingMore().ShouldBeTrue();
    }

    [Fact]
    public async Task ClipBenchs_own_write_is_not_reported_but_the_next_copy_by_someone_else_is()
    {
        await StartAsync();
        var writer = new ClipboardWriter(clipboard.Api, TimeProvider.System);

        (await writer.WriteAsync(new TextContent("our own paste"), Ct)).IsSuccess.ShouldBeTrue();
        NothingMore().ShouldBeTrue("the marker keeps ClipBench from recording its own paste");

        await PutAsync("a real copy");
        NextEvent().ShouldBe(clipboard.Api.SequenceNumber);
    }

    [Fact]
    public async Task After_dispose_a_copy_is_not_reported()
    {
        await StartAsync();
        _listener.Dispose();

        await PutAsync("nobody is listening");

        NothingMore().ShouldBeTrue();
    }
}
