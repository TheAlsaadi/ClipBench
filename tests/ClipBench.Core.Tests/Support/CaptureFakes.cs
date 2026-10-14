using ClipBench.Core.Capture;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.Results;

namespace ClipBench.Core.Tests.Support;

internal sealed class FakeClipboardListener : IClipboardListener
{
    public Result StartResult { get; set; } = Result.Success();

    public int StartCalls { get; private set; }

    public int Disposed { get; private set; }

    public event EventHandler<ClipboardChangedEventArgs>? Changed;

    public bool HasSubscribers => Changed is not null;

    public Task<Result> StartAsync(CancellationToken cancellationToken)
    {
        StartCalls++;
        return Task.FromResult(StartResult);
    }

    public void Raise(long sequenceNumber) => Changed?.Invoke(this, new ClipboardChangedEventArgs(sequenceNumber));

    public void Dispose() => Disposed++;
}

internal sealed class FakeClipboardReader : IClipboardReader
{
    private int _active;
    private int _calls;

    /// <summary>What a read does. The default reads nothing.</summary>
    public Func<CaptureFilter, CancellationToken, Task<Result<ClipboardCapture>>> Handler { get; set; } =
        (_, _) => Task.FromResult(Result.Failure<ClipboardCapture>(new ResultError(ErrorCode.ClipboardEmpty, "empty")));

    public List<CaptureFilter> Filters { get; } = [];

    public int Calls => Volatile.Read(ref _calls);

    public int MaxConcurrentReads { get; private set; }

    public void Returns(Result<ClipboardCapture> result) => Handler = (_, _) => Task.FromResult(result);

    public async Task<Result<ClipboardCapture>> ReadAsync(CaptureFilter filter, CancellationToken cancellationToken)
    {
        var active = Interlocked.Increment(ref _active);
        MaxConcurrentReads = Math.Max(MaxConcurrentReads, active);
        _ = Interlocked.Increment(ref _calls);
        lock (Filters)
        {
            Filters.Add(filter);
        }

        try
        {
            return await Handler(filter, cancellationToken);
        }
        finally
        {
            _ = Interlocked.Decrement(ref _active);
        }
    }
}

/// <summary>Collects what is logged and lets a test wait until a number of entries has arrived.</summary>
internal sealed class FakeActivityLog : IActivityLog
{
    private readonly Lock _gate = new();
    private readonly List<(int Count, TaskCompletionSource Done)> _waiters = [];
    private readonly List<Entry> _entries = [];

    public IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public void Write(ActivityLevel level, ActivityEvent activity, ActivityDetails details = default)
    {
        lock (_gate)
        {
            _entries.Add(new Entry(level, activity, details));
            foreach (var waiter in _waiters.Where(waiter => _entries.Count >= waiter.Count).ToList())
            {
                _ = waiter.Done.TrySetResult();
                _ = _waiters.Remove(waiter);
            }
        }
    }

    /// <summary>Completes once at least this many entries have been written.</summary>
    public Task WaitForCountAsync(int count, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_entries.Count >= count)
            {
                return Task.CompletedTask;
            }

            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((count, done));
            return done.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    internal readonly record struct Entry(ActivityLevel Level, ActivityEvent Activity, ActivityDetails Details);
}
