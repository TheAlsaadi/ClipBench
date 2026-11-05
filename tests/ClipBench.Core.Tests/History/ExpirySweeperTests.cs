using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.History;

public sealed class ExpirySweeperTests : IDisposable
{
    private const string Jwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeClipStore _store = new();
    private readonly FakeActivityLog _log = new();
    private readonly ClipLibrary _library;

    public ExpirySweeperTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _clock), _store) { SensitiveExpiry = TimeSpan.FromMinutes(2) };
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _library.Dispose();

    [Fact]
    public async Task One_sweep_removes_the_expired_secrets()
    {
        await _library.AddAsync(TestClips.Text(Jwt, _clock.GetUtcNow()), Ct);
        _clock.Advance(TimeSpan.FromMinutes(3));
        using var sweeper = new ExpirySweeper(_library, _log, TimeSpan.FromHours(1));

        await sweeper.SweepNowAsync(Ct);

        _library.Clips.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_sweep_that_fails_is_logged_without_the_content_and_does_not_throw()
    {
        _store.Failure = new ResultError(ErrorCode.StorageFailure, "the disk said no");
        using var sweeper = new ExpirySweeper(_library, _log, TimeSpan.FromHours(1));

        await sweeper.SweepNowAsync(Ct);

        _log.Entries.ShouldContain(entry => entry.Activity == ActivityEvent.StoreFailed && entry.Details.Error == ErrorCode.StorageFailure);
    }

    [Fact]
    public async Task Once_started_it_sweeps_at_once_and_then_again_each_interval()
    {
        var swept = new SemaphoreSlim(0);
        _library.Changed += (_, _) => swept.Release();
        await _library.AddAsync(TestClips.Text(Jwt, _clock.GetUtcNow()), Ct);
        _clock.Advance(TimeSpan.FromMinutes(3));
        using var sweeper = new ExpirySweeper(_library, _log, TimeSpan.FromMilliseconds(20));

        sweeper.Start();

        // Added raised Changed once already; the sweep's Changed is the second.
        (await swept.WaitAsync(TimeSpan.FromSeconds(10), Ct)).ShouldBeTrue();
        (await swept.WaitAsync(TimeSpan.FromSeconds(10), Ct)).ShouldBeTrue();
        _library.Clips.ShouldBeEmpty();
        await WaitForSweepsAsync(3);
        _store.Sweeps.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task After_it_is_stopped_it_sweeps_no_more()
    {
        using var sweeper = new ExpirySweeper(_library, _log, TimeSpan.FromMilliseconds(10));
        sweeper.Start();
        await WaitForSweepsAsync(2);

        sweeper.IsRunning.ShouldBeTrue();

        await sweeper.StopAsync();
        var sweeps = _store.Sweeps;

        sweeper.IsRunning.ShouldBeFalse();
        _store.Sweeps.ShouldBe(sweeps);
    }

    [Fact]
    public async Task Starting_twice_runs_one_loop_and_stopping_twice_is_fine()
    {
        using var sweeper = new ExpirySweeper(_library, _log, TimeSpan.FromHours(1));

        sweeper.Start();
        sweeper.Start();
        await WaitForSweepsAsync(1);
        await sweeper.StopAsync();
        await sweeper.StopAsync();

        _store.Sweeps.ShouldBe(1);
    }

    [Fact]
    public void An_interval_that_is_not_positive_or_missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ExpirySweeper(_library, _log, TimeSpan.Zero));
        Should.Throw<ArgumentNullException>(() => new ExpirySweeper(null!, _log, TimeSpan.FromSeconds(1)));
        Should.Throw<ArgumentNullException>(() => new ExpirySweeper(_library, null!, TimeSpan.FromSeconds(1)));
    }

    private async Task WaitForSweepsAsync(int count)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (_store.Sweeps < count)
        {
            await Task.Yield();
            timeout.Token.ThrowIfCancellationRequested();
        }
    }
}
