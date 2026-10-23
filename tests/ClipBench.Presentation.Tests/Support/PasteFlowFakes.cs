using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.Notifications;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;

namespace ClipBench.Presentation.Tests.Support;

internal sealed class FakeForegroundTracker : IForegroundTracker
{
    public Result<ForegroundTarget> Next { get; set; } =
        Result.Success(new ForegroundTarget(new WindowHandle(0x1234), 42, "notepad.exe", BlocksInputFromUs: false));

    public int Captures { get; private set; }

    public Result<ForegroundTarget> Capture()
    {
        Captures++;
        return Next;
    }

    /// <summary>The windows that were asked to come to the front.</summary>
    public List<WindowHandle> Restored { get; } = [];

    public bool Restore(WindowHandle window)
    {
        Restored.Add(window);
        return true;
    }
}

internal sealed class FakePasteInjector : IPasteInjector
{
    public Result Outcome { get; set; } = Result.Success();

    public Exception? Throws { get; set; }

    public List<PasteRequest> Requests { get; } = [];

    public Task<Result> PasteAsync(PasteRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Throws is { } exception ? Task.FromException<Result>(exception) : Task.FromResult(Outcome);
    }
}

internal sealed class FakeClipboardWriter : IClipboardWriter
{
    public Result<long> Outcome { get; set; } = Result.Success(7L);

    public List<ClipContent> Written { get; } = [];

    public Task<Result<long>> WriteAsync(ClipContent content, CancellationToken cancellationToken)
    {
        Written.Add(content);
        return Task.FromResult(Outcome);
    }
}

internal sealed class FakeNotifier : IUserNotifier
{
    public List<(string Title, string Message)> Shown { get; } = [];

    public void Show(string title, string message) => Shown.Add((title, message));
}

internal sealed class FakeActivityLog : IActivityLog
{
    public List<(ActivityLevel Level, ActivityEvent Activity, ActivityDetails Details)> Entries { get; } = [];

    public void Write(ActivityLevel level, ActivityEvent activity, ActivityDetails details = default) =>
        Entries.Add((level, activity, details));
}
