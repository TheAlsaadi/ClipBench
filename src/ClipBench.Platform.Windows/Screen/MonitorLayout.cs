using System.Collections.Immutable;
using ClipBench.Core.Screen;

namespace ClipBench.Platform.Windows.Screen;

/// <summary>Where the monitors are, asked of Windows each time because monitors come and go.</summary>
public sealed class MonitorLayout : IMonitorLayout
{
    private readonly IScreenApi _screen;

    internal MonitorLayout(IScreenApi screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        _screen = screen;
    }

    /// <inheritdoc/>
    public ImmutableArray<MonitorBounds> GetMonitors() => _screen.GetMonitors();
}
