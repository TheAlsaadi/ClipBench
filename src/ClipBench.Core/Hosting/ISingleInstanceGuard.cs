namespace ClipBench.Core.Hosting;

/// <summary>
/// Makes sure only one copy of ClipBench runs for the signed-in user. A second launch tells the first to
/// open the popup and then exits.
/// </summary>
public interface ISingleInstanceGuard : IDisposable
{
    /// <summary>True when this process is the running copy, and false when another copy already was.</summary>
    bool IsFirstInstance { get; }

    /// <summary>Raised in the running copy when another launch asks it to open the popup, on a thread-pool thread.</summary>
    event EventHandler? ShowRequested;

    /// <summary>Asks the running copy to open the popup. Call it from a second launch, then exit.</summary>
    /// <returns>True when the running copy was reached.</returns>
    bool SignalFirstInstance();
}
