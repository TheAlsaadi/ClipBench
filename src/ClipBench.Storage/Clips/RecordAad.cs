using System.Text;
using ClipBench.Core.Clips;

namespace ClipBench.Storage.Clips;

/// <summary>
/// The associated data that binds a sealed value to the place it was written. A value moved to another row,
/// another column or another format fails authentication. The layout is part of the stored format and is
/// pinned by tests: <c>"CB1C" || id(16, big-endian) || length(1) || column || format(1)</c> for a clip, and
/// <c>"CB1M" || length(1) || name</c> for a meta row.
/// </summary>
internal static class RecordAad
{
    private static readonly byte[] _clipTag = "CB1C"u8.ToArray();
    private static readonly byte[] _metaTag = "CB1M"u8.ToArray();

    /// <summary>The data for one encrypted column of a clip row.</summary>
    /// <param name="id">The clip id.</param>
    /// <param name="column">The column name.</param>
    /// <param name="format">The clip format stored in the same row.</param>
    /// <returns>The bytes to pass as associated data.</returns>
    public static byte[] ForClip(ClipId id, string column, ClipFormat format)
    {
        var name = Name(column);
        var aad = new byte[_clipTag.Length + 16 + 1 + name.Length + 1];
        _clipTag.CopyTo(aad, 0);
        id.Value.TryWriteBytes(aad.AsSpan(_clipTag.Length, 16), bigEndian: true, out _);
        aad[_clipTag.Length + 16] = (byte)name.Length;
        name.CopyTo(aad, _clipTag.Length + 16 + 1);
        aad[^1] = (byte)format;
        return aad;
    }

    /// <summary>The data for the value of one meta row.</summary>
    /// <param name="name">The row name.</param>
    /// <returns>The bytes to pass as associated data.</returns>
    public static byte[] ForMeta(string name)
    {
        var bytes = Name(name);
        var aad = new byte[_metaTag.Length + 1 + bytes.Length];
        _metaTag.CopyTo(aad, 0);
        aad[_metaTag.Length] = (byte)bytes.Length;
        bytes.CopyTo(aad, _metaTag.Length + 1);
        return aad;
    }

    private static byte[] Name(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        var bytes = Encoding.ASCII.GetBytes(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bytes.Length, byte.MaxValue, nameof(value));
        return bytes;
    }
}
