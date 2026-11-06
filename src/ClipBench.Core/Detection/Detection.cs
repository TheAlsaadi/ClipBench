namespace ClipBench.Core.Detection;

/// <summary>What a piece of text turned out to be.</summary>
public enum DetectedKind
{
    /// <summary>Nothing in particular.</summary>
    None,

    /// <summary>A color written as hex, rgb or hsl.</summary>
    Color,

    /// <summary>One web address.</summary>
    Link,

    /// <summary>One email address.</summary>
    Email,

    /// <summary>One phone number.</summary>
    Phone,

    /// <summary>Source code or markup.</summary>
    Code,
}

/// <summary>A color: each channel from 0 to 255, with the alpha (255 is fully opaque).</summary>
/// <param name="Red">The red channel.</param>
/// <param name="Green">The green channel.</param>
/// <param name="Blue">The blue channel.</param>
/// <param name="Alpha">The alpha channel.</param>
public readonly record struct ColorSwatch(byte Red, byte Green, byte Blue, byte Alpha)
{
    /// <summary>True when the color is fully opaque.</summary>
    public bool IsOpaque => Alpha == 255;

    /// <summary>The color as <c>#RRGGBB</c>, or <c>#RRGGBBAA</c> when it is not opaque.</summary>
    public string Hex => IsOpaque ? $"#{Red:X2}{Green:X2}{Blue:X2}" : $"#{Red:X2}{Green:X2}{Blue:X2}{Alpha:X2}";
}

/// <summary>What <see cref="ContentDetector"/> found. Only what the text says: nothing is fetched or looked up.</summary>
public sealed record Detection
{
    /// <summary>Nothing was found.</summary>
    public static Detection None { get; } = new();

    /// <summary>What the text is.</summary>
    public DetectedKind Kind { get; init; }

    /// <summary>The color, for <see cref="DetectedKind.Color"/>.</summary>
    public ColorSwatch? Color { get; init; }

    /// <summary>The web address to open, written out in full, for <see cref="DetectedKind.Link"/>. It is only opened when the user asks.</summary>
    public string? Address { get; init; }

    /// <summary>The guessed language, for <see cref="DetectedKind.Code"/>.</summary>
    public string? Language { get; init; }
}
