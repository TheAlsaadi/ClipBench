using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.Hotkeys;
using ClipBench.Platform.Windows.Windowing;

namespace ClipBench.Platform.Windows.IntegrationTests.Support;

/// <summary>
/// A window that the test sends messages to by hand. It also acts as the window thread: work runs inline,
/// and the fake APIs below fail a test if they are called from outside it.
/// </summary>
internal sealed class FakeWindow : IMessageSource, IThreadInvoker
{
    private readonly Dictionary<uint, List<Func<nint, nint, bool>>> _handlers = [];

    /// <summary>True while work given to <see cref="InvokeAsync{T}"/> or a message handler is running.</summary>
    public bool OnWindowThread { get; private set; }

    public void AddHandler(uint message, Func<nint, nint, bool> handler)
    {
        if (!_handlers.TryGetValue(message, out var list))
        {
            _handlers[message] = list = [];
        }

        list.Add(handler);
    }

    public Task<T> InvokeAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var was = OnWindowThread;
        OnWindowThread = true;
        try
        {
            return Task.FromResult(work());
        }
        catch (Exception exception)
        {
            return Task.FromException<T>(exception);
        }
        finally
        {
            OnWindowThread = was;
        }
    }

    /// <summary>Sends a message as Windows would, on the window thread.</summary>
    /// <returns>True when a handler dealt with it.</returns>
    public bool Send(uint message, nint wParam = 0, nint lParam = 0)
    {
        var was = OnWindowThread;
        OnWindowThread = true;
        try
        {
            return _handlers.TryGetValue(message, out var list) && list.Aggregate(false, (handled, handler) => handler(wParam, lParam) | handled);
        }
        finally
        {
            OnWindowThread = was;
        }
    }
}

internal sealed class FakeNotifications(FakeWindow window) : IClipboardNotifications
{
    public bool Accepts { get; set; } = true;

    public int Subscriptions { get; private set; }

    public int Unsubscriptions { get; private set; }

    public bool Subscribe()
    {
        window.OnWindowThread.ShouldBeTrue("clipboard notifications must be set up on the window thread");
        Subscriptions++;
        return Accepts;
    }

    public void Unsubscribe()
    {
        window.OnWindowThread.ShouldBeTrue("clipboard notifications must be removed on the window thread");
        Unsubscriptions++;
    }
}

internal sealed class FakeHotkeyApi(FakeWindow window) : IHotkeyApi
{
    public List<(int Id, uint Modifiers, uint Key)> Registered { get; } = [];

    public List<int> Released { get; } = [];

    /// <summary>The Windows error number to fail with, or 0 to succeed.</summary>
    public int FailWith { get; set; }

    public bool RefuseUnregister { get; set; }

    public bool Register(int id, uint modifiers, uint virtualKey, out int error)
    {
        window.OnWindowThread.ShouldBeTrue("hotkeys must be registered on the window thread");
        error = FailWith;
        if (FailWith != 0)
        {
            return false;
        }

        Registered.Add((id, modifiers, virtualKey));
        return true;
    }

    public bool Unregister(int id)
    {
        window.OnWindowThread.ShouldBeTrue("hotkeys must be released on the window thread");
        if (RefuseUnregister)
        {
            return false;
        }

        Released.Add(id);
        return true;
    }
}
