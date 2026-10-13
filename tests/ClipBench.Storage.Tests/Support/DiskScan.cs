using System.Text;

namespace ClipBench.Storage.Tests.Support;

/// <summary>Reads every file under a folder as raw bytes and looks for byte sequences in them.</summary>
internal static class DiskScan
{
    /// <summary>Everything on disk under the folder, one entry per file. Files held open by a database are read too.</summary>
    public static IReadOnlyList<(string Name, byte[] Bytes)> Files(string folder) =>
    [
        .. Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(path =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return (Path.GetRelativePath(folder, path), memory.ToArray());
        }),
    ];

    public static bool Contains(string folder, byte[] needle) =>
        Files(folder).Any(file => file.Bytes.AsSpan().IndexOf(needle) >= 0);

    /// <summary>The names of the files that contain the text, as UTF-8 or as UTF-16 (the two ways .NET holds text).</summary>
    public static string[] FilesContaining(string folder, string text)
    {
        var utf8 = Encoding.UTF8.GetBytes(text);
        var utf16 = Encoding.Unicode.GetBytes(text);
        return
        [
            .. Files(folder)
                .Where(file => file.Bytes.AsSpan().IndexOf(utf8) >= 0 || file.Bytes.AsSpan().IndexOf(utf16) >= 0)
                .Select(file => file.Name),
        ];
    }
}
