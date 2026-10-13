using System.Buffers.Binary;
using System.Text;
using ClipBench.Core.Results;

namespace ClipBench.Platform.Windows.Formats;

/// <summary>
/// Reads and writes the file list that Explorer puts on the clipboard (CF_HDROP): a 20-byte DROPFILES
/// header, then NUL-separated Unicode paths ending with an extra NUL. Only the paths are read. The files
/// are never opened, checked or run.
/// </summary>
internal static class DropFiles
{
    /// <summary>The most files in one list. The same limit applies to stored file lists.</summary>
    public const int MaxFiles = 100_000;

    // The longest path Windows accepts, in UTF-16 units (an extended-length path).
    private const int MaxPathLength = 32_767;
    private const int HeaderSize = 20;

    /// <summary>Reads the paths out of CF_HDROP data.</summary>
    /// <param name="data">The data as it is on the clipboard.</param>
    /// <param name="maxBytes">The most bytes to accept. Larger data is refused without being read.</param>
    /// <returns>
    /// The paths in order, or <see cref="ErrorCode.ClipboardUnreadable"/> when the header, an offset or a path is
    /// not valid. One bad path refuses the whole list, because a partial list would paste the wrong files.
    /// </returns>
    public static Result<IReadOnlyList<string>> Parse(ReadOnlySpan<byte> data, int maxBytes)
    {
        if (data.Length > maxBytes)
        {
            return Unreadable("The file list on the clipboard is too large to keep.");
        }

        if (data.Length < HeaderSize)
        {
            return Unreadable("The file list on the clipboard is damaged.");
        }

        var namesOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        var isUnicode = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4)) != 0;

        // A list in the ANSI form would have to be read in the sender's code page. Every current program
        // writes Unicode, so the old form is treated as damage instead of guessed at. An offset inside the
        // header needs no check of its own: the header's non-zero Unicode flag makes any list read from
        // there fail below.
        if (!isUnicode || namesOffset > data.Length)
        {
            return Unreadable("The file list on the clipboard is damaged.");
        }

        // A dangling odd byte decodes to U+FFFD after the end of the list, which the padding check refuses.
        return ReadPaths(Encoding.Unicode.GetString(data[(int)namesOffset..]));
    }

    /// <summary>Writes a file list in the layout Explorer uses.</summary>
    /// <param name="paths">The paths. At least one, none empty and none containing a NUL.</param>
    /// <returns>The CF_HDROP data.</returns>
    public static byte[] Build(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            throw new ArgumentException("A file list needs at least one path.", nameof(paths));
        }

        if (paths.Any(path => string.IsNullOrEmpty(path) || path.Contains('\0')))
        {
            throw new ArgumentException("A path cannot be empty or contain a NUL.", nameof(paths));
        }

        var names = Encoding.Unicode.GetBytes(string.Concat(paths.Select(path => path + "\0")) + "\0");
        var data = new byte[HeaderSize + names.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16, 4), 1);
        names.CopyTo(data, HeaderSize);
        return data;
    }

    private static Result<IReadOnlyList<string>> ReadPaths(string text)
    {
        var paths = new List<string>();
        var position = 0;
        while (true)
        {
            var end = text.IndexOf('\0', position);
            if (end < 0)
            {
                return Unreadable("The file list on the clipboard is damaged.");
            }

            if (end == position)
            {
                break;
            }

            var path = text[position..end];
            if (path.Length > MaxPathLength || ContainsControlCharacter(path) || string.IsNullOrWhiteSpace(path) || paths.Count == MaxFiles)
            {
                return Unreadable("The file list on the clipboard is damaged.");
            }

            paths.Add(path);
            position = end + 1;
        }

        // Nothing but padding may follow the end of the list: a name after an empty one is a damaged list.
        if (paths.Count == 0 || text.AsSpan(position).ContainsAnyExcept('\0'))
        {
            return Unreadable("The file list on the clipboard is damaged.");
        }

        return Result.Success<IReadOnlyList<string>>(paths);
    }

    // Windows file names cannot contain the characters 0 to 31.
    private static bool ContainsControlCharacter(string path) => path.AsSpan().ContainsAnyInRange('\0', '\u001F');

    private static Result<IReadOnlyList<string>> Unreadable(string message) =>
        Result.Failure<IReadOnlyList<string>>(new ResultError(ErrorCode.ClipboardUnreadable, message));
}
