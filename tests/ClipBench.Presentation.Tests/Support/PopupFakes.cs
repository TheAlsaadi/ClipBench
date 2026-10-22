using ClipBench.Core.Clips;
using ClipBench.Presentation.Popup;

namespace ClipBench.Presentation.Tests.Support;

/// <summary>Runs posted work at once, on the calling thread.</summary>
internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public int Posts { get; private set; }

    public void Post(Action action)
    {
        Posts++;
        action();
    }
}

/// <summary>Records the order of what the popup asks of the paste flow.</summary>
internal sealed class FakePasteFlow : IPasteFlow
{
    /// <summary>Shared with the test, so it can tell what happened before or after a paste.</summary>
    public List<string> Order { get; } = [];

    public List<Clip> Pasted { get; } = [];

    public int Begins { get; private set; }

    /// <summary>Runs inside <see cref="Begin"/>, so a test can look at the state at that moment.</summary>
    public Action? OnBegin { get; set; }

    /// <summary>What each call to <see cref="Begin"/> asked for.</summary>
    public List<bool> RecordedTargets { get; } = [];

    public void Begin(bool recordTarget = true)
    {
        Begins++;
        RecordedTargets.Add(recordTarget);
        Order.Add("begin");
        OnBegin?.Invoke();
    }

    public Task PasteAsync(Clip clip, CancellationToken cancellationToken)
    {
        Pasted.Add(clip);
        Order.Add("paste");
        return Task.CompletedTask;
    }

    /// <summary>Content that was composed from several clips and pasted.</summary>
    public List<ClipContent> PastedContent { get; } = [];

    public Task PasteContentAsync(ClipContent content, CancellationToken cancellationToken)
    {
        PastedContent.Add(content);
        Order.Add("paste");
        return Task.CompletedTask;
    }
}
