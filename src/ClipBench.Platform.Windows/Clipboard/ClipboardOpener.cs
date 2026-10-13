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

/// <summary>Opens the clipboard, waiting for another program to let go of it.</summary>
internal static class ClipboardOpener
{
    /// <summary>
    /// Tries to open the clipboard, waiting with a doubling delay between attempts. The waiting happens here,
    /// outside the open clipboard, so the caller's open-and-close stays on one thread without a pause in it.
    /// </summary>
    /// <param name="api">The Windows surface.</param>
    /// <param name="policy">How long to keep trying.</param>
    /// <param name="time">The clock the waits use.</param>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <returns>The open clipboard, or null when it stayed busy for every attempt.</returns>
    public static async Task<IClipboardSession?> OpenAsync(IClipboardApi api, ClipboardRetryPolicy policy, TimeProvider time, CancellationToken cancellationToken)
    {
        var delay = policy.InitialDelay;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = api.TryOpen();
            if (session is not null)
            {
                return session;
            }

            if (attempt >= policy.Attempts)
            {
                return null;
            }

            await Task.Delay(delay, time, cancellationToken).ConfigureAwait(false);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, policy.MaxDelay.Ticks));
        }
    }
}
