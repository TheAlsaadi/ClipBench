using System.Security.Cryptography;

namespace ClipBench.Platform.Windows.Clipboard;

/// <summary>
/// A raw copy of everything on the clipboard, format by format, so that it can be put back exactly as it
/// was: used to restore what the user had copied after a paste. Privacy markers are formats like any other, so
/// restoring a password manager's entry restores its "do not record" marker too. The copies are overwritten
/// when the snapshot is disposed.
/// </summary>
internal sealed class ClipboardSnapshot : IDisposable
{
    private readonly List<(uint Format, byte[] Data)> _entries;

    private ClipboardSnapshot(List<(uint Format, byte[] Data)> entries) => _entries = entries;

    /// <summary>The copies, so a test can check they are overwritten on disposal.</summary>
    internal IReadOnlyList<byte[]> BuffersForTesting => [.. _entries.Select(entry => entry.Data)];

    /// <summary>
    /// Copies every format that is plain memory. Formats that are handles to graphics objects are left out
    /// (Windows makes them again from the bitmap formats), and so are formats that cannot be read.
    /// </summary>
    /// <param name="session">The open clipboard.</param>
    /// <param name="maxBytes">The most bytes to copy in all.</param>
    /// <returns>The snapshot, or null when the clipboard holds more than the limit (nothing is kept then).</returns>
    public static ClipboardSnapshot? Take(IClipboardSession session, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(session);

        List<(uint Format, byte[] Data)> entries = [];
        long total = 0;
        foreach (var format in session.Formats())
        {
            if (!ClipboardFormats.IsMemoryBlock(format.Id))
            {
                continue;
            }

            var size = session.SizeOf(format.Id);
            if (size is null)
            {
                continue;
            }

            total += size.Value;
            if (total > maxBytes)
            {
                Zero(entries);
                return null;
            }

            var data = session.Read(format.Id, size.Value);
            if (data is not null)
            {
                entries.Add((format.Id, data));
            }
        }

        return new ClipboardSnapshot(entries);
    }

    /// <summary>Empties the clipboard and puts the copied formats back, in their original order.</summary>
    /// <param name="session">The open clipboard.</param>
    /// <returns>True when everything was put back. False when Windows refused to empty the clipboard (nothing was changed) or refused a format (the others were still restored).</returns>
    public bool Restore(IClipboardSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!session.Empty())
        {
            return false;
        }

        var complete = true;
        foreach (var (format, data) in _entries)
        {
            complete &= session.Set(format, data);
        }

        return complete;
    }

    /// <summary>Overwrites the copies.</summary>
    public void Dispose() => Zero(_entries);


    private static void Zero(List<(uint Format, byte[] Data)> entries)
    {
        foreach (var (_, data) in entries)
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }
}
