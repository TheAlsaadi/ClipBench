namespace ClipBench.Platform.Windows.Windowing;

/// <summary>Raises events without letting one subscriber's bug reach the others.</summary>
internal static class Subscribers
{
    /// <summary>Calls each subscriber in turn. One that throws is skipped; the rest are still called.</summary>
    /// <typeparam name="TArgs">The type of the event arguments.</typeparam>
    /// <param name="handler">The event field.</param>
    /// <param name="sender">The object raising the event.</param>
    /// <param name="args">The arguments.</param>
    public static void Raise<TArgs>(EventHandler<TArgs>? handler, object sender, TArgs args)
    {
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList().Cast<EventHandler<TArgs>>())
        {
            try
            {
                subscriber(sender, args);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A subscriber's bug must not take the others down. Phase 4 adds logging for this.
            }
        }
    }
}
