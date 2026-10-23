using System.Collections.Immutable;
using ClipBench.Core.Clips;
using ClipBench.Presentation.Boards;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace ClipBench.App;

/// <summary>Small helpers the row template binds to: an icon for each kind of clip and a thumbnail for a picture.</summary>
internal static class Glyphs
{
    private const int ThumbnailWidth = 96;

    /// <summary>The Segoe Fluent Icons character for a kind of clip.</summary>
    /// <param name="format">The kind.</param>
    /// <returns>The glyph.</returns>
    public static string For(ClipFormat format) => format switch
    {
        ClipFormat.Text => "",
        ClipFormat.Html => "",
        ClipFormat.Rtf => "",
        ClipFormat.Image => "",
        ClipFormat.Files => "",
        _ => "",
    };

    /// <summary>The Segoe Fluent Icons character for an entry of the sidebar.</summary>
    /// <param name="scope">What the entry shows.</param>
    /// <returns>The glyph: a clock for the history, a pin for the pinned clips, a paste icon for the snippets and a library for a board.</returns>
    public static string ForScope(PopupScope scope) => scope switch
    {
        PopupScope.History => ((char)0xE81C).ToString(),
        PopupScope.Pinned => ((char)0xE718).ToString(),
        PopupScope.Snippets => ((char)0xE77F).ToString(),
        _ => ((char)0xE8F1).ToString(),
    };

    /// <summary>Decodes a picture at thumbnail size, so a large screenshot does not cost a full-size bitmap per row.</summary>
    /// <param name="png">The PNG data, or empty for no picture.</param>
    /// <returns>The thumbnail, or null when there is no picture or it cannot be decoded.</returns>
    public static BitmapImage? ThumbnailOf(ImmutableArray<byte> png)
    {
        if (png.IsDefaultOrEmpty)
        {
            return null;
        }

        try
        {
            var image = new BitmapImage { DecodePixelWidth = ThumbnailWidth };
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes([.. png]);
                _ = writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                writer.DetachStream();
            }

            stream.Seek(0);
            image.SetSource(stream);
            return image;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
