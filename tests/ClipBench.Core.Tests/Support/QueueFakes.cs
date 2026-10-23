using ClipBench.Core.Hotkeys;
using ClipBench.Core.Notifications;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;

namespace ClipBench.Core.Tests.Support;

/// <summary>
/// Shortcuts held in memory. Every call completes at once, so a press handled by the code under test runs to its
/// end before <see cref="Press"/> returns, unless the code waits on something the test controls.
/// </summary>
internal sealed class FakeHotkeyService : IHotkeyService
{
    private readonly Dictionary<int, Hotkey> _registered = [];
    private int _nextId = 100;

    public event EventHandler<HotkeyPressedEventArgs>? Pressed;

    /// <summary>Every claim and release, in order, as "+Ctrl+V" or "-Esc".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Shortcuts that another program holds: claiming them fails.</summary>
    public HashSet<Hotkey> Taken { get; } = [];

    /// <summary>When set, the next claim of this shortcut fails, once.</summary>
    public Hotkey? FailNextClaimOf { get; set; }

    public bool IsDisposed { get; private set; }

    public IReadOnlyCollection<Hotkey> Registered => [.. _registered.Values];

    public bool IsRegistered(Hotkey hotkey) => _registered.ContainsValue(hotkey);

    public Task<Result<HotkeyId>> RegisterAsync(Hotkey hotkey, CancellationToken cancellationToken)
    {
        if (Taken.Contains(hotkey) || FailNextClaimOf == hotkey)
        {
            FailNextClaimOf = null;
            return Task.FromResult(Result.Failure<HotkeyId>(new ResultError(ErrorCode.HotkeyUnavailable, "taken")));
        }

        var id = _nextId++;
        _registered[id] = hotkey;
        Calls.Add("+" + hotkey);
        return Task.FromResult(Result.Success(new HotkeyId(id)));
    }

    public Task<Result> UnregisterAsync(HotkeyId id, CancellationToken cancellationToken)
    {
        if (!_registered.Remove(id.Value, out var hotkey))
        {
            return Task.FromResult(Result.Failure(new ResultError(ErrorCode.HotkeyNotRegistered, "unknown")));
        }

        Calls.Add("-" + hotkey);
        return Task.FromResult(Result.Success());
    }

    /// <summary>Presses a shortcut the way Windows would: only a registered one is reported.</summary>
    public void Press(Hotkey hotkey)
    {
        foreach (var (id, registered) in _registered.ToList())
        {
            if (registered == hotkey)
            {
                Pressed?.Invoke(this, new HotkeyPressedEventArgs(new HotkeyId(id)));
            }
        }
    }

    /// <summary>Reports a press of any id, registered or not, as a late event from the message pump would.</summary>
    public void PressId(int id) => Pressed?.Invoke(this, new HotkeyPressedEventArgs(new HotkeyId(id)));

    public int SubscriberCount => Pressed?.GetInvocationList().Length ?? 0;

    public void Dispose() => IsDisposed = true;
}

/// <summary>Records paste requests and answers them as the test says.</summary>
internal sealed class FakePasteInjector : IPasteInjector
{
    public List<PasteRequest> Requests { get; } = [];

    /// <summary>Runs inside every paste, before it answers, so a test can look at the state at that moment.</summary>
    public Action<PasteRequest>? OnPaste { get; set; }

    /// <summary>Answers a paste. The default succeeds at once.</summary>
    public Func<PasteRequest, Task<Result>> Answer { get; set; } = _ => Task.FromResult(Result.Success());

    public Task<Result> PasteAsync(PasteRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        OnPaste?.Invoke(request);
        return Answer(request);
    }
}

/// <summary>Reports a fixed window as the one with focus, or no window at all.</summary>
internal sealed class FakeForegroundTracker : IForegroundTracker
{
    public static readonly WindowHandle Window = new(0x4242);

    public bool HasFocusWindow { get; set; } = true;

    public Result<ForegroundTarget> Capture() => HasFocusWindow
        ? Result.Success(new ForegroundTarget(Window, 77, "form.exe", BlocksInputFromUs: false))
        : Result.Failure<ForegroundTarget>(new ResultError(ErrorCode.TargetNotFound, "nothing has focus"));

    public bool Restore(WindowHandle window) => HasFocusWindow;

    public bool IsForeground(WindowHandle window) => HasFocusWindow && window == Window;
}

/// <summary>Collects the notices shown to the user.</summary>
internal sealed class FakeNotifier : IUserNotifier
{
    public List<(string Title, string Message)> Shown { get; } = [];

    public void Show(string title, string message) => Shown.Add((title, message));
}
