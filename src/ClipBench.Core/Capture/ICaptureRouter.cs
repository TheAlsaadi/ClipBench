using ClipBench.Core.Clips;

namespace ClipBench.Core.Capture;

/// <summary>Gets the first look at each copy the capture filter accepted, before the history does.</summary>
public interface ICaptureRouter
{
    /// <summary>Offers a copy.</summary>
    /// <param name="clip">The copy, as a clip.</param>
    /// <returns>True when the copy should also be saved in the history as a clip of its own.</returns>
    bool Offer(Clip clip);
}
