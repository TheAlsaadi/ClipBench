using System.Collections.Immutable;
using System.Text;

namespace ClipBench.Core.Clips;

/// <summary>A list of file paths. Only the paths are stored; the files are never read.</summary>
public sealed record FilesContent : ClipContent
{
    private readonly long _sizeInBytes;

    /// <summary>Creates file list content. The paths are copied.</summary>
    /// <param name="paths">The file paths, in order.</param>
    public FilesContent(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var copy = paths.ToImmutableArray();
        if (copy.IsEmpty)
        {
            throw new ArgumentException("A file list needs at least one path.", nameof(paths));
        }

        if (copy.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A file path cannot be blank.", nameof(paths));
        }

        Paths = copy;
        _sizeInBytes = copy.Sum(path => (long)Encoding.UTF8.GetByteCount(path));
    }

    /// <summary>The file paths, in order.</summary>
    public ImmutableArray<string> Paths { get; }

    /// <inheritdoc/>
    public override ClipFormat Format => ClipFormat.Files;

    /// <inheritdoc/>
    public override long SizeInBytes => _sizeInBytes;

    /// <inheritdoc/>
    public override string? PlainText => string.Join("\r\n", Paths);

    /// <summary>Two file lists are equal when they hold the same paths in the same order.</summary>
    /// <param name="other">The list to compare with.</param>
    /// <returns>True when both hold the same paths.</returns>
    public bool Equals(FilesContent? other) => other is not null && Paths.AsSpan().SequenceEqual(other.Paths.AsSpan());

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var path in Paths)
        {
            hash.Add(path, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
