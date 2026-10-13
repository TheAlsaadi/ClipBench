namespace ClipBench.Storage.Tests.Support;

/// <summary>Deterministic keys for tests. These are never used outside tests.</summary>
internal static class TestKeys
{
    /// <summary>A 32-byte key whose bytes are all derived from the seed, so two seeds give two keys.</summary>
    public static byte[] Master(byte seed)
    {
        var key = new byte[32];
        for (var i = 0; i < key.Length; i++)
        {
            key[i] = unchecked((byte)((seed * 31) + (i * 7) + 1));
        }

        return key;
    }
}
