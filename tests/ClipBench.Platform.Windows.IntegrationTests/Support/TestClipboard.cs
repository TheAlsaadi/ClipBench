using ClipBench.Platform.Windows.Clipboard;

namespace ClipBench.Platform.Windows.IntegrationTests.Support;

/// <summary>Opens a real clipboard for a test's own work, the same way the app does, so open and close stay on one thread.</summary>
internal static class TestClipboard
{
    /// <summary>Runs the work while the clipboard is open. The work must not await.</summary>
    public static async Task<T> UseAsync<T>(IClipboardApi api, Func<IClipboardSession, T> work, CancellationToken cancellationToken)
    {
        var use = await ClipboardOpener.UseAsync(api, ClipboardRetryPolicy.Default, TimeProvider.System, work, cancellationToken);
        use.Opened.ShouldBeTrue("the clipboard stayed busy for the whole retry period");
        return use.Value!;
    }
}
