using System.Collections.Immutable;

namespace ClipBench.Core.Screen;

/// <summary>Tells where the monitors are.</summary>
public interface IMonitorLayout
{
    /// <summary>Lists the monitors of the desktop as they are now.</summary>
    /// <returns>Each monitor's whole rectangle on the virtual screen and its scale, or none when Windows would not say.</returns>
    ImmutableArray<MonitorBounds> GetMonitors();
}
