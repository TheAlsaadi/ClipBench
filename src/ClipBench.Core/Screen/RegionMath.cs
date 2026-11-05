using System.Collections.Immutable;
using ClipBench.Core.Results;

namespace ClipBench.Core.Screen;

/// <summary>One monitor: where it is on the virtual screen, in device pixels, and how much larger than 96 dpi it is drawn.</summary>
public readonly record struct MonitorBounds
{
    /// <summary>Creates a monitor.</summary>
    /// <param name="bounds">The monitor's rectangle in device pixels. It has an area; monitors left of or above the main one have negative corners.</param>
    /// <param name="dpiScale">1.0 at 96 dpi, 1.5 at 144 dpi. Greater than zero.</param>
    public MonitorBounds(PixelRect bounds, double dpiScale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bounds.Width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bounds.Height, 0);
        if (!(dpiScale > 0) || !double.IsFinite(dpiScale))
        {
            throw new ArgumentOutOfRangeException(nameof(dpiScale), dpiScale, "The scale must be a number greater than zero.");
        }

        Bounds = bounds;
        DpiScale = dpiScale;
    }

    /// <summary>The monitor's rectangle in device pixels.</summary>
    public PixelRect Bounds { get; }

    /// <summary>The scale of the monitor: 1.0 at 96 dpi.</summary>
    public double DpiScale { get; }
}

/// <summary>
/// The arithmetic of choosing a region of the screen to read text from: pure functions, so every monitor layout can be
/// tested without a screen. Everything is in device pixels on the virtual screen unless a name says window units.
/// </summary>
public static class RegionMath
{
    /// <summary>The shortest side of a region, in pixels. A smaller drag is a slip of the hand, not text.</summary>
    public const int MinSide = 8;

    /// <summary>The longest side of a region, in pixels. This is the most the text recognizer of Windows reads.</summary>
    public const int MaxSide = 10_000;

    /// <summary>Turns the two ends of a drag into the region to capture, kept inside the monitor it was made on.</summary>
    /// <param name="start">Where the button went down.</param>
    /// <param name="end">Where the button came up. It can be above or left of <paramref name="start"/>.</param>
    /// <param name="monitor">The monitor the drag was made on.</param>
    /// <returns>
    /// The rectangle, cut at the monitor's edges, or <see cref="ErrorCode.RegionTooSmall"/> when a side is shorter than
    /// <see cref="MinSide"/> (also when the drag lies outside the monitor), or <see cref="ErrorCode.RegionTooLarge"/> when a
    /// side is longer than <see cref="MaxSide"/>.
    /// </returns>
    public static Result<PixelRect> FromDrag(PixelPoint start, PixelPoint end, PixelRect monitor)
    {
        // Long arithmetic: the edges of a rectangle can sit near the limits of an int.
        var left = Math.Max((long)Math.Min(start.X, end.X), monitor.X);
        var top = Math.Max((long)Math.Min(start.Y, end.Y), monitor.Y);
        var right = Math.Min((long)Math.Max(start.X, end.X), monitor.Right);
        var bottom = Math.Min((long)Math.Max(start.Y, end.Y), monitor.Bottom);
        var width = right - left;
        var height = bottom - top;

        if (width < MinSide || height < MinSide)
        {
            return Result.Failure<PixelRect>(new ResultError(
                ErrorCode.RegionTooSmall,
                $"Choose a larger area: at least {MinSide} by {MinSide} pixels, on one monitor."));
        }

        if (width > MaxSide || height > MaxSide)
        {
            return Result.Failure<PixelRect>(new ResultError(
                ErrorCode.RegionTooLarge,
                $"Choose a smaller area: the text reader handles at most {MaxSide} pixels on a side."));
        }

        return Result.Success(new PixelRect((int)left, (int)top, (int)width, (int)height));
    }

    /// <summary>Turns a size in units (a line two units thick) into device pixels for a monitor.</summary>
    /// <param name="units">The size in units at 96 dpi.</param>
    /// <param name="scale">The monitor's scale: 1.0 at 96 dpi. Greater than zero.</param>
    /// <returns>The size in pixels, rounded to the nearest pixel and never less than one, so a thin line never vanishes.</returns>
    public static int Scaled(int units, double scale)
    {
        if (!(scale > 0) || !double.IsFinite(scale))
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The scale must be a number greater than zero.");
        }

        return Math.Max(1, (int)Math.Round(units * scale, MidpointRounding.AwayFromZero));
    }

    /// <summary>Gives the part of a selection that falls on a monitor, relative to the monitor's own top-left corner.</summary>
    /// <param name="selection">The selection in device pixels on the virtual screen.</param>
    /// <param name="monitor">The monitor's rectangle in device pixels.</param>
    /// <returns>The part inside the monitor, measured from the monitor's corner, or null when none of it is on that monitor.</returns>
    public static PixelRect? ToLocal(PixelRect selection, PixelRect monitor)
    {
        var left = Math.Max((long)selection.X, monitor.X);
        var top = Math.Max((long)selection.Y, monitor.Y);
        var right = Math.Min((long)selection.Right, monitor.Right);
        var bottom = Math.Min((long)selection.Bottom, monitor.Bottom);
        if (right <= left || bottom <= top)
        {
            return null;
        }

        return new PixelRect((int)(left - monitor.X), (int)(top - monitor.Y), (int)(right - left), (int)(bottom - top));
    }

    /// <summary>Finds the monitor a point is on.</summary>
    /// <param name="monitors">Every monitor.</param>
    /// <param name="point">The point in device pixels.</param>
    /// <returns>The monitor whose rectangle holds the point (its right and bottom edges are outside it), or null.</returns>
    public static MonitorBounds? MonitorAt(ImmutableArray<MonitorBounds> monitors, PixelPoint point)
    {
        foreach (var monitor in monitors.IsDefault ? [] : monitors)
        {
            var bounds = monitor.Bounds;
            if (point.X >= bounds.X && point.X < bounds.Right && point.Y >= bounds.Y && point.Y < bounds.Bottom)
            {
                return monitor;
            }
        }

        return null;
    }
}
