using ClipBench.Core.Hotkeys;
using ClipBench.Platform.Windows.Pasting;

namespace ClipBench.Platform.Windows.IntegrationTests.Support;

/// <summary>Windows' window and keyboard behaviour, scripted: who has focus, whether a request for focus is obeyed, which modifier keys are held.</summary>
internal sealed class FakeInput : IWindowApi, IKeyboardApi
{
    private int _pollsSinceFocusRequest = -1;
    private int _modifierPolls;

    /// <summary>The window that has focus.</summary>
    public nint Foreground { get; set; } = 0x900;

    /// <summary>The windows that exist.</summary>
    public HashSet<nint> Windows { get; } = [0x100, 0x900];

    public Dictionary<nint, uint> ProcessOf { get; } = new() { [0x100] = 4321, [0x900] = 999 };

    /// <summary>How many times focus is looked at after it was requested before it actually moves. -1: it never moves.</summary>
    public int FocusMovesAfterPolls { get; set; }

    public List<nint> FocusRequests { get; } = [];

    /// <summary>Run when focus is requested, so a test can have another program act at that moment.</summary>
    public Action? OnFocusRequest { get; set; }

    /// <summary>The modifier keys held down.</summary>
    public HotkeyModifiers Held { get; set; }

    /// <summary>How many times the held keys are looked at before they are let go. -1: they stay down.</summary>
    public int ReleaseAfterPolls { get; set; } = -1;

    /// <summary>Run each time the held keys are looked at, with how many times that has been, so a test can have the user act meanwhile.</summary>
    public Action<int>? OnModifierPoll { get; set; }

    /// <summary>When set, only this many of the events sent are accepted.</summary>
    public int? AcceptOnly { get; set; }

    /// <summary>Run when keys are sent, so a test can look at the clipboard at that moment.</summary>
    public Action? OnSend { get; set; }

    public List<KeyEvent[]> Sent { get; } = [];

    public nint GetForegroundWindow()
    {
        if (_pollsSinceFocusRequest >= 0 && FocusMovesAfterPolls >= 0 && _pollsSinceFocusRequest++ >= FocusMovesAfterPolls)
        {
            Foreground = FocusRequests[^1];
            _pollsSinceFocusRequest = -1;
        }

        return Foreground;
    }

    public bool IsWindow(nint window) => Windows.Contains(window);

    public bool SetForegroundWindow(nint window)
    {
        FocusRequests.Add(window);
        _pollsSinceFocusRequest = 0;
        OnFocusRequest?.Invoke();
        return true;
    }

    public uint GetWindowProcessId(nint window) => ProcessOf.GetValueOrDefault(window);

    public HotkeyModifiers GetHeldModifiers()
    {
        OnModifierPoll?.Invoke(_modifierPolls + 1);
        if (ReleaseAfterPolls >= 0 && _modifierPolls++ >= ReleaseAfterPolls)
        {
            Held = HotkeyModifiers.None;
        }

        return Held;
    }

    public int Send(ReadOnlySpan<KeyEvent> events)
    {
        Sent.Add(events.ToArray());
        OnSend?.Invoke();
        return AcceptOnly ?? events.Length;
    }
}

internal sealed class FakeIntegrity : IProcessIntegrity
{
    public int OwnLevel { get; set; } = 0x2000;

    public Dictionary<uint, IntegrityResult> Levels { get; } = new()
    {
        [4321] = new IntegrityResult(IntegrityOutcome.Known, 0x2000),
        [999] = new IntegrityResult(IntegrityOutcome.Known, 0x2000),
    };

    public IntegrityResult Query(uint processId) => Levels.GetValueOrDefault(processId, new IntegrityResult(IntegrityOutcome.NotFound));
}
