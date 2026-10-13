namespace ClipBench.Platform.Windows.Formats;

/// <summary>The CRC-32 checksum that every PNG chunk ends with (the same polynomial as zip and Ethernet).</summary>
internal static class Crc32
{
    private const uint Polynomial = 0xEDB88320;

    private static readonly uint[] _table = BuildTable();

    /// <summary>Computes the checksum of some bytes.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The checksum.</returns>
    public static uint Compute(ReadOnlySpan<byte> data) => Update(0, data);

    /// <summary>Continues a checksum over more bytes, so a chunk's type and data need not be joined first.</summary>
    /// <param name="crc">The checksum so far, or 0 to start.</param>
    /// <param name="data">The next bytes.</param>
    /// <returns>The checksum of everything so far.</returns>
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        var value = ~crc;
        foreach (var current in data)
        {
            value = _table[(value ^ current) & 0xFF] ^ (value >> 8);
        }

        return ~value;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            var value = n;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? Polynomial ^ (value >> 1) : value >> 1;
            }

            table[n] = value;
        }

        return table;
    }
}
