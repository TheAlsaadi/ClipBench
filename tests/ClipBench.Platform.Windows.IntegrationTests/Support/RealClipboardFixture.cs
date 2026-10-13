using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.Windowing;

namespace ClipBench.Platform.Windows.IntegrationTests.Support;

/// <summary>
/// The real Windows clipboard for the integration tests. Before the first test it copies what the user had
/// on the clipboard (every memory format, privacy markers included), and after the last it puts it back, so
/// running the tests does not cost anyone what they had copied. Handle-based formats (a picture copied as a
/// bitmap object) are made again by Windows from the bitmap formats that are kept.
/// </summary>
public sealed class RealClipboardFixture : IAsyncLifetime
{
    private const long SnapshotLimit = 256L * 1024 * 1024;

    private ClipboardSnapshot? _saved;

    /// <summary>ClipBench's own window and clipboard binding: what the app will use.</summary>
    internal MessageWindow Window { get; private set; } = null!;

    internal Win32ClipboardApi Api { get; private set; } = null!;

    /// <summary>A second window and binding that stands in for another program.</summary>
    internal MessageWindow ForeignWindow { get; private set; } = null!;

    internal Win32ClipboardApi ForeignApi { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Window = MessageWindow.Create().Value;
        ForeignWindow = MessageWindow.Create().Value;
        Api = new Win32ClipboardApi(Window.Handle);
        ForeignApi = new Win32ClipboardApi(ForeignWindow.Handle);

        var saved = await ClipboardOpener.UseAsync(ForeignApi, ClipboardRetryPolicy.Default, TimeProvider.System, session => ClipboardSnapshot.Take(session, SnapshotLimit), CancellationToken.None);

        // These tests replace the clipboard. If what the user has cannot be saved, they do not run at all.
        _saved = saved.Value ?? throw new InvalidOperationException(
            "The real-clipboard tests were not run because your clipboard could not be saved first (it was busy, or holds more than 256 MB), and they replace it. Copy something small and run them again.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_saved is not null)
        {
            var saved = _saved;
            await ClipboardOpener.UseAsync(ForeignApi, ClipboardRetryPolicy.Default, TimeProvider.System, session => saved.Restore(session), CancellationToken.None);
            saved.Dispose();
        }

        Window.Dispose();
        ForeignWindow.Dispose();
    }
}

/// <summary>All tests that touch the real clipboard share it, so they run one at a time.</summary>
[CollectionDefinition("Clipboard", DisableParallelization = true)]
public sealed class ClipboardTestGroup : ICollectionFixture<RealClipboardFixture>;
