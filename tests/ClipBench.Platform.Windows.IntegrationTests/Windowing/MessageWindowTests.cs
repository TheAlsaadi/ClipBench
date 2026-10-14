using ClipBench.Platform.Windows.Windowing;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace ClipBench.Platform.Windows.IntegrationTests.Windowing;

// A message-only window is invisible and takes no focus, so these run everywhere Windows does, including CI.
public class MessageWindowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_hidden_top_level_window_hears_a_broadcast_and_a_message_only_window_does_not()
    {
        // The taskbar tells every top-level window when it is created again (so a tray icon can come back), but it
        // does not tell message-only windows. A tray icon therefore needs the first kind.
        var message = PInvoke.RegisterWindowMessage("ClipBench.Test.Broadcast." + Guid.NewGuid().ToString("N"));
        message.ShouldBeGreaterThan(0u);
        using var topLevel = MessageWindow.Create(messageOnly: false).Value;
        using var messageOnly = MessageWindow.Create().Value;
        var topLevelHeard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messageOnlyHeard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        topLevel.AddHandler(message, (_, _) => topLevelHeard.TrySetResult());
        messageOnly.AddHandler(message, (_, _) => messageOnlyHeard.TrySetResult());

        bool posted = PInvoke.PostMessage(new HWND(0xFFFF), message, default, default);   // HWND_BROADCAST
        posted.ShouldBeTrue();

        await topLevelHeard.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        // The broadcast reaches every top-level window before it is over. Give a message-only window the chance to hear it too.
        await Task.Delay(TimeSpan.FromMilliseconds(300), Ct);
        messageOnlyHeard.Task.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public void A_top_level_window_is_a_real_window_that_is_gone_after_dispose()
    {
        var window = MessageWindow.Create(messageOnly: false).Value;
        var handle = window.Handle;

        Exists(handle).ShouldBeTrue();

        window.Dispose();

        Exists(handle).ShouldBeFalse();
    }

    [Fact]
    public void Creating_a_window_gives_a_real_handle_that_is_gone_after_dispose()
    {
        var window = MessageWindow.Create().Value;
        var handle = window.Handle;

        Exists(handle).ShouldBeTrue();
        handle.ShouldNotBe(IntPtr.Zero);

        window.Dispose();

        Exists(handle).ShouldBeFalse();
    }

    private static bool Exists(nint handle) => PInvoke.IsWindow(new HWND(handle));

    [Fact]
    public async Task Work_runs_on_the_one_thread_that_owns_the_window_and_not_on_the_callers_thread()
    {
        using var window = MessageWindow.Create().Value;
        var caller = Environment.CurrentManagedThreadId;

        var first = await window.InvokeAsync(() => Environment.CurrentManagedThreadId, Ct);
        var second = await window.InvokeAsync(() => Environment.CurrentManagedThreadId, Ct);

        first.ShouldNotBe(caller);
        second.ShouldBe(first);
    }

    [Fact]
    public async Task Work_asked_for_from_many_threads_all_runs_and_never_two_at_once()
    {
        using var window = MessageWindow.Create().Value;
        var active = 0;
        var mostAtOnce = 0;
        var completed = 0;

        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(
            () => window.InvokeAsync(() =>
            {
                var now = Interlocked.Increment(ref active);
                mostAtOnce = Math.Max(mostAtOnce, now);
                Thread.SpinWait(200);
                Interlocked.Decrement(ref active);
                return Interlocked.Increment(ref completed);
            }, Ct),
            Ct)));

        completed.ShouldBe(200);
        mostAtOnce.ShouldBe(1);
    }

    [Fact]
    public async Task An_exception_in_the_work_reaches_the_caller_and_the_window_keeps_working()
    {
        using var window = MessageWindow.Create().Value;

        var failing = window.InvokeAsync<int>(() => throw new InvalidOperationException("boom"), Ct);
        (await Should.ThrowAsync<InvalidOperationException>(failing)).Message.ShouldBe("boom");

        (await window.InvokeAsync(() => 7, Ct)).ShouldBe(7);
    }

    [Fact]
    public async Task Work_that_was_cancelled_before_it_ran_does_not_run()
    {
        using var window = MessageWindow.Create().Value;
        var gate = new ManualResetEventSlim();
        var blocker = window.InvokeAsync(() =>
        {
            gate.Wait(Ct);
            return 0;
        }, Ct);
        using var cancellation = new CancellationTokenSource();
        var ran = false;
        var queued = window.InvokeAsync(() =>
        {
            ran = true;
            return 1;
        }, cancellation.Token);

        await cancellation.CancelAsync();
        gate.Set();
        await blocker;

        await Should.ThrowAsync<TaskCanceledException>(queued);
        (await window.InvokeAsync(() => 0, Ct)).ShouldBe(0, "the queue is still alive");
        ran.ShouldBeFalse();
    }

    [Fact]
    public async Task A_message_posted_to_the_window_reaches_the_handler_on_the_window_thread()
    {
        using var window = MessageWindow.Create().Value;
        var windowThread = await window.InvokeAsync(() => Environment.CurrentManagedThreadId, Ct);
        var received = new TaskCompletionSource<(int Thread, nint WParam, nint LParam)>(TaskCreationOptions.RunContinuationsAsynchronously);
        const uint message = 0x8000 + 77;   // WM_APP + 77
        window.AddHandler(message, (wParam, lParam) =>
        {
            received.TrySetResult((Environment.CurrentManagedThreadId, wParam, lParam));
            return true;
        });

        bool posted = PInvoke.PostMessage(new HWND(window.Handle), message, new WPARAM(11), new LPARAM(22));
        posted.ShouldBeTrue();

        var (thread, wParam, lParam) = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        thread.ShouldBe(windowThread);
        wParam.ShouldBe(11);
        lParam.ShouldBe(22);
    }

    [Fact]
    public async Task A_handler_that_throws_does_not_take_the_window_down()
    {
        using var window = MessageWindow.Create().Value;
        const uint message = 0x8000 + 78;
        var calls = 0;
        window.AddHandler(message, (_, _) =>
        {
            calls++;
            throw new InvalidOperationException("handler failure");
        });

        PInvoke.PostMessage(new HWND(window.Handle), message, default, default);
        PInvoke.PostMessage(new HWND(window.Handle), message, default, default);

        // A later call is answered only after both posted messages were handled, because the queue is in order.
        (await window.InvokeAsync(() => calls, Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Dispose_fails_work_that_has_not_run_and_later_requests_fail_at_once()
    {
        var window = MessageWindow.Create().Value;
        var gate = new ManualResetEventSlim();
        _ = window.InvokeAsync(() =>
        {
            gate.Wait(Ct);
            return 0;
        }, Ct);
        var waiting = window.InvokeAsync(() => 1, Ct);

        // The window thread is busy, so dispose has to wait for it. Release it only once disposal has begun.
        var disposing = Task.Run(window.Dispose, Ct);
        SpinWait.SpinUntil(() => window.IsDisposed, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        gate.Set();
        await disposing;

        await Should.ThrowAsync<ObjectDisposedException>(waiting);
        await Should.ThrowAsync<ObjectDisposedException>(window.InvokeAsync(() => 2, Ct));
    }

    [Fact]
    public async Task When_Windows_has_no_room_for_the_wake_up_only_that_request_fails_and_work_already_waiting_still_runs()
    {
        using var window = MessageWindow.Create().Value;
        using var gate = new ManualResetEventSlim();
        var blocker = window.InvokeAsync(() =>
        {
            gate.Wait(Ct);
            return 0;
        }, Ct);
        var waiting = window.InvokeAsync(() => 5, Ct);

        // Windows keeps at most 10,000 posted messages for a thread. The thread is busy, so they pile up.
        for (var i = 0; i < 10_500; i++)
        {
            PInvoke.PostMessage(new HWND(window.Handle), 0x8000 + 99, default, default);
        }

        var refused = window.InvokeAsync(() => 6, Ct);

        var failure = await Should.ThrowAsync<InvalidOperationException>(refused);
        failure.ShouldNotBeOfType<ObjectDisposedException>("the window is alive; only the wake-up could not be queued");
        failure.Message.ShouldContain("queue");
        gate.Set();
        await blocker;
        (await waiting).ShouldBe(5, "work that was already waiting is not a casualty");
        (await window.InvokeAsync(() => 7, Ct)).ShouldBe(7, "the window is healthy afterwards");
    }
    [Fact]
    public async Task Disposing_from_the_window_thread_itself_does_not_wait_for_itself()
    {
        var window = MessageWindow.Create().Value;
        var handle = window.Handle;

        var result = await window.InvokeAsync(
            () =>
            {
                window.Dispose();
                return 1;
            },
            Ct).WaitAsync(TimeSpan.FromSeconds(2), Ct);

        result.ShouldBe(1);
        SpinWait.SpinUntil(() => !Exists(handle), TimeSpan.FromSeconds(5)).ShouldBeTrue("the window is destroyed once the work that disposed it returns");
    }

    [Fact]
    public void Disposing_twice_is_fine()
    {
        var window = MessageWindow.Create().Value;

        window.Dispose();

        Should.NotThrow(window.Dispose);
    }

    [Fact]
    public void Many_windows_can_exist_at_once_and_each_has_its_own_handle()
    {
        var windows = Enumerable.Range(0, 5).Select(_ => MessageWindow.Create().Value).ToList();
        try
        {
            windows.Select(window => window.Handle).Distinct().Count().ShouldBe(5);
        }
        finally
        {
            windows.ForEach(window => window.Dispose());
        }
    }
}
