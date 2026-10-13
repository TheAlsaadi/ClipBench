using ClipBench.Core.Notifications;

namespace ClipBench.App.Composition;

/// <summary>Shows nothing. Stands in until the tray icon, which shows notices, is started.</summary>
internal sealed class NullNotifier : IUserNotifier
{
    /// <inheritdoc/>
    public void Show(string title, string message)
    {
    }
}
