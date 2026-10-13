namespace ClipBench.Core.Notifications;

/// <summary>Tells the user something that happened in the background, such as a paste that did not work.</summary>
public interface IUserNotifier
{
    /// <summary>
    /// Shows a short notice. The text is always written by the app, never taken from clip content, so
    /// a notice cannot leak what the user copied.
    /// </summary>
    /// <param name="title">The heading.</param>
    /// <param name="message">One or two sentences.</param>
    void Show(string title, string message);
}
