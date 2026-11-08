namespace ClipBench.Platform.Windows.Clipboard;

/// <summary>Asks Windows for a program's path into a buffer.</summary>
/// <param name="buffer">Where the path goes.</param>
/// <param name="length">How many characters were written.</param>
/// <returns>False when Windows could not give the path, for example because the buffer was too small.</returns>
internal delegate bool PathQuery(Span<char> buffer, out int length);

/// <summary>
/// Reads a program's path with a buffer that is large enough for nearly every path, and once more with the longest
/// path Windows allows when that fails. A path that could not be read leaves the owner unknown, and an unknown owner
/// would let a program on the exclusion list be recorded.
/// </summary>
internal static class ProcessImagePath
{
    /// <summary>The first buffer, in characters.</summary>
    public const int UsualLength = 1_024;

    /// <summary>The second buffer, in characters: the longest path Windows allows.</summary>
    public const int LongestLength = 32_767;

    /// <summary>Reads a path.</summary>
    /// <param name="query">The call to Windows.</param>
    /// <returns>The path, or null when Windows could not give it with either buffer.</returns>
    public static string? Read(PathQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        Span<char> usual = stackalloc char[UsualLength];
        if (query(usual, out var length))
        {
            return new string(usual[..length]);
        }

        // Too large for the stack; a path this long is rare.
        var longest = new char[LongestLength];
        return query(longest, out length) ? new string(longest, 0, length) : null;
    }
}
