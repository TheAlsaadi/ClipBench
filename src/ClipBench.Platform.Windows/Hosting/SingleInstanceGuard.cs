using System.Text.RegularExpressions;
using ClipBench.Core.Hosting;
using ClipBench.Platform.Windows.Windowing;

namespace ClipBench.Platform.Windows.Hosting;

/// <summary>
/// Makes sure only one copy of ClipBench runs for the signed-in user. The copy that creates a named mutex is the
/// running one; a second launch finds it already there, sets a named event, and exits. The names start with
/// <c>Local\</c>, so they belong to the user's session: two people signed in to one PC each get their own copy.
/// </summary>
internal sealed partial class SingleInstanceGuard : ISingleInstanceGuard
{
    /// <summary>The name the app uses.</summary>
    public const string AppName = "ClipBench";

    private const int MaxNameLength = 100;

    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _signal;
    private readonly EventPump<bool> _pump;
    private readonly RegisteredWaitHandle? _wait;
    private readonly Lock _gate = new();
    private EventHandler? _showRequested;
    private bool _pending;
    private int _disposed;

    private SingleInstanceGuard(string name)
    {
        // Not owned: the mutex only has to exist. Owning it would tie it to one thread, and it would be abandoned
        // when that thread ended. A copy started with "Run as administrator" makes its objects with a higher integrity
        // label, and an ordinary copy is refused them: that is another running copy, and a launch must not crash for it.
        var created = false;
        try
        {
            _mutex = new Mutex(false, $@"Local\{name}.Instance", out created);
        }
        catch (UnauthorizedAccessException)
        {
            _mutex = null;
        }

        IsFirstInstance = created;

        // Both launches open or create the event, so whichever comes first is fine and a signal is never lost.
        try
        {
            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.ShowPopup");
        }
        catch (UnauthorizedAccessException)
        {
            _signal = null;
        }

        _pump = new EventPump<bool>(_ => Raise());

        if (IsFirstInstance && _signal is not null)
        {
            _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) => OnSignal(), null, Timeout.Infinite, executeOnlyOnce: false);
        }
    }

    /// <inheritdoc/>
    public bool IsFirstInstance { get; }

    /// <summary>True when a request arrived while nobody listened and is waiting for the first listener. For tests.</summary>
    internal bool HasPendingRequest
    {
        get
        {
            lock (_gate)
            {
                return _pending;
            }
        }
    }

    /// <inheritdoc/>
    public event EventHandler? ShowRequested
    {
        add
        {
            bool replay;
            lock (_gate)
            {
                _showRequested += value;
                replay = _pending;
                _pending = false;
            }

            // A second launch can arrive while the running copy is still setting itself up. It is not forgotten.
            if (replay)
            {
                _pump.Post(true);
            }
        }

        remove
        {
            lock (_gate)
            {
                _showRequested -= value;
            }
        }
    }

    /// <summary>Takes part in the guard for the app: one copy per signed-in user.</summary>
    /// <returns>The guard. Dispose it when the app exits.</returns>
    public static SingleInstanceGuard ForCurrentUser() => Acquire(AppName);

    /// <summary>Takes part in the guard for a name.</summary>
    /// <param name="name">Letters, digits, dots, dashes and underscores, up to 100 characters. Nothing that could reach another namespace.</param>
    /// <returns>The guard. Its <see cref="IsFirstInstance"/> says whether this is the running copy.</returns>
    public static SingleInstanceGuard Acquire(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length is 0 or > MaxNameLength || !SafeName().IsMatch(name))
        {
            throw new ArgumentException("A name is letters, digits, dots, dashes and underscores, up to 100 characters.", nameof(name));
        }

        return new SingleInstanceGuard(name);
    }

    /// <inheritdoc/>
    public bool SignalFirstInstance()
    {
        if (IsFirstInstance || _signal is null || Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        try
        {
            return _signal.Set();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _wait?.Unregister(null);
        _pump.Dispose();
        _signal?.Dispose();
        _mutex?.Dispose();
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex SafeName();

    private void OnSignal()
    {
        bool listening;
        lock (_gate)
        {
            listening = _showRequested is not null;
            _pending = !listening;
        }

        if (listening)
        {
            _pump.Post(true);
        }
    }

    private void Raise()
    {
        EventHandler? handler;
        lock (_gate)
        {
            handler = _showRequested;
        }

        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                subscriber(this, EventArgs.Empty);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A subscriber's bug must not stop the next request. Phase 4 adds logging for this.
            }
        }
    }
}
