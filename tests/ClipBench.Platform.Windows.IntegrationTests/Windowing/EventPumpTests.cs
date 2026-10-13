using ClipBench.Platform.Windows.Windowing;

namespace ClipBench.Platform.Windows.IntegrationTests.Windowing;

public class EventPumpTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Events_are_delivered_in_the_order_they_were_posted()
    {
        List<int> received = [];
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new EventPump<int>(value =>
        {
            received.Add(value);
            if (value == 999)
            {
                done.TrySetResult();
            }
        });

        for (var i = 0; i <= 999; i++)
        {
            pump.Post(i);
        }

        await done.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        received.ShouldBe(Enumerable.Range(0, 1000));
    }

    [Fact]
    public async Task An_event_is_not_delivered_before_Post_has_returned()
    {
        // Comparing thread numbers cannot show this: the poster is a pool thread too, and once it yields at an await
        // the pool may run the delivery on that very thread (about 1 round in 20 here). What matters is that the
        // poster is never held up, so the subscriber is made to wait for a signal that is only given after Post returns.
        // A delivery that ran inside Post would wait for a signal that cannot come, and time out.
        using var postReturned = new ManualResetEventSlim();
        var seenAfterPostReturned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new EventPump<int>(_ => seenAfterPostReturned.TrySetResult(postReturned.Wait(TimeSpan.FromSeconds(5))));

        pump.Post(1);
        postReturned.Set();

        (await seenAfterPostReturned.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_delivery_that_throws_does_not_stop_the_events_after_it()
    {
        List<int> received = [];
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new EventPump<int>(value =>
        {
            if (value == 1)
            {
                throw new InvalidOperationException("a subscriber bug");
            }

            received.Add(value);
            if (value == 3)
            {
                done.TrySetResult();
            }
        });

        pump.Post(1);
        pump.Post(2);
        pump.Post(3);

        await done.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        received.ShouldBe([2, 3]);
    }

    [Fact]
    public async Task A_slow_delivery_does_not_block_the_poster()
    {
        var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pump = new EventPump<int>(_ =>
        {
            started.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(30));
        });

        pump.Post(1);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var posting = Task.Run(() => pump.Post(2), Ct);

        (await Task.WhenAny(posting, Task.Delay(TimeSpan.FromSeconds(5), Ct))).ShouldBe(posting);
        release.Set();
    }

    [Fact]
    public async Task Nothing_is_delivered_after_dispose()
    {
        var count = 0;
        var firstSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pump = new EventPump<int>(_ =>
        {
            Interlocked.Increment(ref count);
            firstSeen.TrySetResult();
        });
        pump.Post(1);
        await firstSeen.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // Dispose returns only once delivery has stopped, so what is posted afterwards has nowhere to go and there is nothing to wait for.
        pump.Dispose();
        pump.Post(2);
        pump.Post(3);

        count.ShouldBe(1);
    }

    [Fact]
    public void Disposing_twice_is_fine()
    {
        var pump = new EventPump<int>(_ => { });

        pump.Dispose();

        Should.NotThrow(pump.Dispose);
    }
}
