namespace ClipBench.Platform.Windows.Windowing;

/// <summary>Clean-up that has to happen on a given thread, from code that may or may not be running on it.</summary>
internal static class ThreadInvokerExtensions
{
    /// <summary>
    /// Runs the work on the invoker's thread and waits for it. From that thread the work runs at once: queueing it behind
    /// the work that is running and waiting for it would block for the whole timeout, with every shortcut and clipboard
    /// notice frozen meanwhile. If the thread is already gone, so is what the work would have cleaned up.
    /// </summary>
    /// <param name="invoker">The thread to run on.</param>
    /// <param name="work">What to do there.</param>
    /// <param name="timeout">The longest to wait when the work has to be queued.</param>
    public static void RunAndWait(this IThreadInvoker invoker, Action work, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(work);

        if (invoker.IsOnThread)
        {
            work();
            return;
        }

        try
        {
            invoker.InvokeAsync(
                () =>
                {
                    work();
                    return true;
                },
                CancellationToken.None).Wait(timeout);
        }
        catch (AggregateException exception) when (exception.InnerException is ObjectDisposedException)
        {
            // The window is already gone, and Windows released what was registered with it.
        }
    }
}
