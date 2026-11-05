namespace ClipBench.Core.Settings;

/// <summary>The smallest and largest value a number setting may have, both included.</summary>
public readonly record struct SettingRange
{
    /// <summary>Creates the range.</summary>
    /// <param name="minimum">The smallest value.</param>
    /// <param name="maximum">The largest value. Not less than <paramref name="minimum"/>.</param>
    public SettingRange(int minimum, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, minimum);
        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>The smallest value.</summary>
    public int Minimum { get; }

    /// <summary>The largest value.</summary>
    public int Maximum { get; }

    /// <summary>Tells whether a value is allowed.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True when the value is within the range.</returns>
    public bool Contains(int value) => value >= Minimum && value <= Maximum;

    /// <summary>Moves a value to the nearest allowed one.</summary>
    /// <param name="value">The value to move.</param>
    /// <returns>The value itself when it is allowed, otherwise the nearest end of the range.</returns>
    public int Clamp(int value) => Math.Clamp(value, Minimum, Maximum);
}
