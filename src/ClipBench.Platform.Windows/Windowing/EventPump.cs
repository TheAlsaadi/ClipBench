using System.Threading.Channels;

namespace ClipBench.Platform.Windows.Windowing;

/// <summary>
/// Hands events to subscribers one at a time, in the order they happened, on a thread-pool thread. The
/// window thread must never wait for a subscriber (a slow one would freeze every hotkey), so it posts here and
/// moves on. A subscriber that throws is skipped and the events after it still arrive.
/// </summary>
/// <typeparam name="T">The type of an event.</typeparam>
internal sealed class EventPump<T> : IDisposable
{
    private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(2);

    private readonly Channel<T> _channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Action<T> _deliver;
    private readonly Task _consumer;
    private int _consumerThread;
    private int _disposed;

    /// <summary>Creates a pump and starts its delivery thread.</summary>
    /// <param name="deliver">Called for each event, in order. Exceptions it throws are ignored.</param>
    public EventPump(Action<T> deliver)
    {
        ArgumentNullException.ThrowIfNull(deliver);
        _deliver = deliver;
        _consumer = Task.Run(ConsumeAsync);
    }

    /// <summary>Queues an event for delivery. Does nothing once the pump is disposed.</summary>
    /// <param name="item">The event.</param>
    public void Post(T item)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            _channel.Writer.TryWrite(item);
        }
    }

    /// <summary>Stops delivering. Events still waiting are dropped.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();

        // A subscriber may dispose the pump from inside its own delivery; waiting for itself would never end.
        if (Environment.CurrentManagedThreadId != Volatile.Read(ref _consumerThread))
        {
            _consumer.Wait(_stopTimeout);
        }
    }

    private async Task ConsumeAsync()
    {
        Volatile.Write(ref _consumerThread, Environment.CurrentManagedThreadId);
        await foreach (var item in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                break;
            }

            try
            {
                Volatile.Write(ref _consumerThread, Environment.CurrentManagedThreadId);
                _deliver(item);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A subscriber's bug must not stop the events that follow. Phase 4 adds logging for this.
            }
        }
    }
}
