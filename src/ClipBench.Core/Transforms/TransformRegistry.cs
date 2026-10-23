using System.Collections.Immutable;

namespace ClipBench.Core.Transforms;

/// <summary>The transforms the picker offers, in the order it shows them.</summary>
public sealed class TransformRegistry
{
    private readonly Dictionary<string, TextTransform> _byId = new(StringComparer.Ordinal);

    /// <summary>Creates a registry.</summary>
    /// <param name="transforms">The transforms, in the order they are shown. Identifiers must be different.</param>
    public TransformRegistry(IEnumerable<TextTransform> transforms)
    {
        ArgumentNullException.ThrowIfNull(transforms);
        All = [.. transforms];
        foreach (var transform in All)
        {
            if (!_byId.TryAdd(transform.Id, transform))
            {
                throw new ArgumentException($"Two transforms have the identifier \"{transform.Id}\".", nameof(transforms));
            }
        }
    }

    /// <summary>The transforms, in the order they are shown.</summary>
    public ImmutableArray<TextTransform> All { get; }

    /// <summary>Looks a transform up by identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The transform, or null when there is none with that identifier.</returns>
    public TextTransform? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _byId.GetValueOrDefault(id);
    }
}
