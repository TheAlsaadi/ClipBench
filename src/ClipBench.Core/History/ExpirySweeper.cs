using ClipBench.Core.Diagnostics;

namespace ClipBench.Core.History;

/// <summary>
/// Removes secrets that have expired, from memory and from disk, when the app starts and then at a steady pace. A sweep
/// that fails is logged and the next one tries again.
/// </summary>
public sealed class ExpirySweeper : IDisposable
{
    private readonly ClipLibrary _library;
    private readonly IActivityLog _log;
    private readonly TimeSpan _interval;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _stop;
    private Task _loop = Task.CompletedTask;

    /// <summary>Creates a sweeper that is not running yet.</summary>
    /// <param name="library">The history to sweep.</param>
    /// <param name="log">Where a failed sweep is recorded. It never receives clip content.</param>
    /// <param name="interval">The time between sweeps. Greater than zero.</param>
    public ExpirySweeper(ClipLibrary library, IActivityLog log, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _library = library;
        _log = log;
        _interval = interval;
    }

    /// <summary>True between <see cref="Start"/> and <see cref="StopAsync"/>.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _stop is not null;
            }
        }
    }

    /// <summary>Starts sweeping: once at once, then every interval. Does nothing when it is already running.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_stop is not null)
            {
                return;
            }

            var stop = new CancellationTokenSource();
            _stop = stop;
            _loop = Task.Run(() => RunAsync(stop.Token));
        }
    }

    /// <summary>Stops sweeping and waits for a sweep in progress. Does nothing when it is not running.</summary>
    /// <returns>A task that completes when no sweep is running.</returns>
    public async Task StopAsync()
    {
        Task loop;
        CancellationTokenSource? stop;
        lock (_gate)
        {
            stop = _stop;
            _stop = null;
            loop = _loop;
        }

        if (stop is null)
        {
            return;
        }

        await stop.CancelAsync().ConfigureAwait(false);
        await loop.ConfigureAwait(false);
        stop.Dispose();
    }

    /// <summary>Sweeps once. A failure is logged, never thrown.</summary>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    /// <returns>A task that completes when the sweep is done.</returns>
    public async Task SweepNowAsync(CancellationToken cancellationToken)
    {
        try
        {
            var swept = await _library.SweepExpiredAsync(cancellationToken).ConfigureAwait(false);
            if (swept.IsFailure)
            {
                _log.Write(ActivityLevel.Warning, ActivityEvent.StoreFailed, new ActivityDetails { Error = swept.Error.Code });
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A sweep must never take the app down; the next one tries again. The message is not logged: it can quote content.
            _log.Write(ActivityLevel.Warning, ActivityEvent.StoreFailed, new ActivityDetails { ExceptionType = exception.GetType() });
        }
    }

    /// <summary>Stops sweeping without waiting.</summary>
    public void Dispose()
    {
        CancellationTokenSource? stop;
        lock (_gate)
        {
            stop = _stop;
            _stop = null;
        }

        stop?.Cancel();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SweepNowAsync(cancellationToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await SweepNowAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }
}
