using System.Collections.Immutable;

namespace ClipBench.Core.Clips;

/// <summary>An image, stored as PNG bytes.</summary>
public sealed record ImageContent : ClipContent
{
    /// <summary>Creates image content. The bytes are copied.</summary>
    /// <param name="png">The PNG data.</param>
    public ImageContent(ReadOnlySpan<byte> png)
    {
        if (png.IsEmpty)
        {
            throw new ArgumentException("An image needs at least one byte.", nameof(png));
        }

        Png = ImmutableArray.Create(png);
    }

    /// <summary>The PNG data.</summary>
    public ImmutableArray<byte> Png { get; }

    /// <inheritdoc/>
    public override ClipFormat Format => ClipFormat.Image;

    /// <inheritdoc/>
    public override long SizeInBytes => Png.Length;

    /// <inheritdoc/>
    public override string? PlainText => null;

    /// <summary>Two images are equal when their bytes are equal.</summary>
    /// <param name="other">The image to compare with.</param>
    /// <returns>True when both hold the same bytes.</returns>
    public bool Equals(ImageContent? other) => other is not null && Png.AsSpan().SequenceEqual(other.Png.AsSpan());

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.AddBytes(Png.AsSpan());
        return hash.ToHashCode();
    }
}
