namespace ClipBench.Core;

/// <summary>Raising an event so that one subscriber's bug cannot stop the others.</summary>
internal static class EventHandlerExtensions
{
    /// <summary>Calls every subscriber in turn. A subscriber that throws is skipped and the others are still told.</summary>
    /// <typeparam name="T">The kind of event data.</typeparam>
    /// <param name="handlers">The event's subscribers, or null when there are none.</param>
    /// <param name="sender">The object that raises the event.</param>
    /// <param name="data">What the subscribers are told.</param>
    public static void RaiseEach<T>(this EventHandler<T>? handlers, object sender, T data)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<T>>())
        {
            try
            {
                handler(sender, data);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A subscriber's bug must not stop the others.
            }
        }
    }

    /// <summary>Calls every subscriber in turn. A subscriber that throws is skipped and the others are still told.</summary>
    /// <param name="handlers">The event's subscribers, or null when there are none.</param>
    /// <param name="sender">The object that raises the event.</param>
    public static void RaiseEach(this EventHandler? handlers, object sender)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler(sender, EventArgs.Empty);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A subscriber's bug must not stop the others, or the change that was already made.
            }
        }
    }
}
