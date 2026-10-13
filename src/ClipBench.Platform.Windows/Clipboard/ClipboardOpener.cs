namespace ClipBench.Platform.Windows.Clipboard;

/// <summary>How long to keep trying when another program has the clipboard open.</summary>
/// <param name="Attempts">The most times to try to open it. At least 1.</param>
/// <param name="InitialDelay">The wait after the first failed attempt. Not negative.</param>
/// <param name="MaxDelay">The longest single wait. The wait doubles after each failure up to this. Not less than the initial wait.</param>
internal sealed record ClipboardRetryPolicy(int Attempts, TimeSpan InitialDelay, TimeSpan MaxDelay)
{
    /// <summary>Eight attempts, waiting 10, 20, 40, 80, 160, 320 and 320 milliseconds between them: about a second in all.</summary>
    public static ClipboardRetryPolicy Default { get; } = new(8, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(320));

    /// <summary>The most times to try to open the clipboard.</summary>
    public int Attempts { get; } = Attempts >= 1 ? Attempts : throw new ArgumentOutOfRangeException(nameof(Attempts), Attempts, "At least one attempt is needed.");

    /// <summary>The wait after the first failed attempt.</summary>
    public TimeSpan InitialDelay { get; } = InitialDelay >= TimeSpan.Zero ? InitialDelay : throw new ArgumentOutOfRangeException(nameof(InitialDelay), InitialDelay, "A wait cannot be negative.");

    /// <summary>The longest single wait.</summary>
    public TimeSpan MaxDelay { get; } = MaxDelay >= InitialDelay ? MaxDelay : throw new ArgumentOutOfRangeException(nameof(MaxDelay), MaxDelay, "The longest wait cannot be shorter than the first.");
}

/// <summary>What came of trying to use the clipboard.</summary>
/// <typeparam name="T">The type of the work's result.</typeparam>
/// <param name="Opened">True when the clipboard was opened and the work ran. False when it stayed busy for every attempt.</param>
/// <param name="Value">The work's result, or the default when the clipboard never opened.</param>
internal readonly record struct ClipboardUse<T>(bool Opened, T? Value);

/// <summary>Opens the clipboard, waiting for another program to let go of it, and runs work while it is open.</summary>
internal static class ClipboardOpener
{
    /// <summary>
    /// Tries to open the clipboard, waiting with a doubling delay between attempts, and runs the work as soon as it
    /// opens. The waiting happens before the clipboard is open, so other programs are never kept waiting by it. The work
    /// runs in the same synchronous stretch as the open and the close, with no await in between: Windows wants the
    /// thread that opened the clipboard to close it, and a session handed back through a task could be closed on another
    /// thread, which leaves the clipboard open for every program until that thread ends.
    /// </summary>
    /// <typeparam name="T">The type of the work's result.</typeparam>
    /// <param name="api">The Windows surface.</param>
    /// <param name="policy">How long to keep trying.</param>
    /// <param name="time">The clock the waits use.</param>
    /// <param name="work">What to do with the open clipboard. It must not wait for anything.</param>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <returns>Whether the clipboard opened and, if so, what the work returned.</returns>
    public static async Task<ClipboardUse<T>> UseAsync<T>(IClipboardApi api, ClipboardRetryPolicy policy, TimeProvider time, Func<IClipboardSession, T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(work);

        var delay = policy.InitialDelay;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = api.TryOpen();
            if (session is not null)
            {
                using (session)
                {
                    return new ClipboardUse<T>(true, work(session));
                }
            }

            if (attempt >= policy.Attempts)
            {
                return new ClipboardUse<T>(false, default);
            }

            await Task.Delay(delay, time, cancellationToken).ConfigureAwait(false);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, policy.MaxDelay.Ticks));
        }
    }
}
