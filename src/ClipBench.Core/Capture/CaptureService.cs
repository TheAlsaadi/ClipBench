using System.Threading.Channels;
using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.History;
using ClipBench.Core.Results;

namespace ClipBench.Core.Capture;

/// <summary>
/// Turns clipboard changes into stored clips: it listens, reads each change through the capture filter, and adds
/// what the filter accepts to the library. Changes are handled one at a time, in order. Pausing sets the filter's
/// incognito rule, so a paused service never reads any content.
/// </summary>
public sealed class CaptureService : ICaptureControl, IDisposable
{
    private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(2);

    private readonly IClipboardListener _listener;
    private readonly IClipboardReader _reader;
    private readonly ClipLibrary _library;
    private readonly IActivityLog _log;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly Channel<long> _changes = Channel.CreateUnbounded<long>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stopping = new();
    private CaptureFilter _filter;
    private Task? _consumer;
    private long _lastRead;
    private bool _disposed;

    /// <summary>Creates the service. Nothing is listened to until <see cref="StartAsync"/>.</summary>
    /// <param name="listener">Tells when the clipboard changes.</param>
    /// <param name="reader">Reads the clipboard through the filter.</param>
    /// <param name="library">Where accepted clips go.</param>
    /// <param name="log">Where activity is recorded. It never receives clip content.</param>
    /// <param name="timeProvider">The clock for the time a clip was copied.</param>
    /// <param name="filter">The starting rules, or null for the defaults.</param>
    public CaptureService(
        IClipboardListener listener,
        IClipboardReader reader,
        ClipLibrary library,
        IActivityLog log,
        TimeProvider timeProvider,
        CaptureFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _listener = listener;
        _reader = reader;
        _library = library;
        _log = log;
        _timeProvider = timeProvider;
        _filter = filter ?? new CaptureFilter();
    }

    /// <summary>The rules in force. The next read uses the current ones.</summary>
    public CaptureFilter Filter
    {
        get
        {
            lock (_gate)
            {
                return _filter;
            }
        }
    }

    /// <inheritdoc/>
    public bool IsPaused => Filter.Incognito;

    /// <inheritdoc/>
    public void SetPaused(bool paused) => UpdateFilter(filter => filter with { Incognito = paused });

    /// <summary>Changes the rules. The next read uses the new ones.</summary>
    /// <param name="change">Makes the new filter from the current one.</param>
    public void UpdateFilter(Func<CaptureFilter, CaptureFilter> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_gate)
        {
            _filter = change(_filter);
        }
    }

    /// <summary>Starts listening. Does nothing when already started.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>Success, or why the clipboard listener could not start.</returns>
    public async Task<Result> StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_consumer is not null)
            {
                return Result.Success();
            }

            _listener.Changed += OnChanged;
            _consumer = Task.Run(() => ConsumeAsync(_stopping.Token), CancellationToken.None);
        }

        var started = await _listener.StartAsync(cancellationToken).ConfigureAwait(false);
        if (started.IsFailure)
        {
            _log.Write(ActivityLevel.Error, ActivityEvent.CaptureListenFailed, new ActivityDetails { Error = started.Error.Code });
            return started;
        }

        _log.Write(ActivityLevel.Information, ActivityEvent.CaptureListening);
        return Result.Success();
    }

    /// <summary>Stops listening. A change that is being handled is given a moment to finish.</summary>
    public void Dispose()
    {
        Task? consumer;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            consumer = _consumer;
        }

        _listener.Changed -= OnChanged;
        _ = _changes.Writer.TryComplete();
        _stopping.Cancel();

        if (consumer is not null)
        {
            _ = consumer.Wait(_stopTimeout);
        }

        _stopping.Dispose();
    }

    private void OnChanged(object? sender, ClipboardChangedEventArgs e) => _ = _changes.Writer.TryWrite(e.SequenceNumber);

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var sequence in _changes.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await HandleAsync(sequence, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    private async Task HandleAsync(long sequence, CancellationToken cancellationToken)
    {
        // An earlier read may already have seen this change: it reads whatever is on the clipboard at that moment.
        if (sequence <= _lastRead)
        {
            return;
        }

        try
        {
            var read = await _reader.ReadAsync(Filter, cancellationToken).ConfigureAwait(false);
            if (read.IsFailure)
            {
                _log.Write(ActivityLevel.Warning, ActivityEvent.CaptureFailed, new ActivityDetails { Error = read.Error.Code });
                return;
            }

            var capture = read.Value;
            _lastRead = Math.Max(_lastRead, capture.SequenceNumber);

            if (capture.Content is not { } content)
            {
                _log.Write(ActivityLevel.Information, ActivityEvent.CaptureRejected, new ActivityDetails { Rejection = capture.Decision.Rejection });
                return;
            }

            var clip = new Clip(ClipId.New(), content, _timeProvider.GetUtcNow(), capture.SourceProcess);
            var added = await _library.AddAsync(clip, cancellationToken).ConfigureAwait(false);
            if (added.IsFailure)
            {
                _log.Write(ActivityLevel.Error, ActivityEvent.StoreFailed, new ActivityDetails { Error = added.Error.Code });
                return;
            }

            _log.Write(
                ActivityLevel.Information,
                ActivityEvent.CaptureStored,
                new ActivityDetails { Format = content.Format, Bytes = content.SizeInBytes });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Only the type is recorded: an exception message can quote what was being read.
            _log.Write(ActivityLevel.Error, ActivityEvent.CaptureFaulted, new ActivityDetails { ExceptionType = exception.GetType() });
        }
    }
}
