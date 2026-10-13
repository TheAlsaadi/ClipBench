using ClipBench.Core.Clips;

namespace ClipBench.Storage.Tests.Support;

/// <summary>Builds clips for tests with sensible defaults.</summary>
internal static class TestClips
{
    public static Clip Text(string text, DateTimeOffset? capturedAt = null, string? source = null, bool pinned = false) =>
        new(ClipId.New(), new TextContent(text), capturedAt ?? FakeTimeProvider.Start, source, pinned);

    public static Clip Html(string html, string text, DateTimeOffset? capturedAt = null) =>
        new(ClipId.New(), new HtmlContent(html, text), capturedAt ?? FakeTimeProvider.Start);

    public static Clip Rtf(string rtf, string text, DateTimeOffset? capturedAt = null) =>
        new(ClipId.New(), new RtfContent(rtf, text), capturedAt ?? FakeTimeProvider.Start);

    public static Clip Image(byte[] png, DateTimeOffset? capturedAt = null) =>
        new(ClipId.New(), new ImageContent(png), capturedAt ?? FakeTimeProvider.Start);

    public static Clip Files(DateTimeOffset? capturedAt = null, params string[] paths) =>
        new(ClipId.New(), new FilesContent(paths), capturedAt ?? FakeTimeProvider.Start);

    /// <summary>One clip of every format, each with distinct content.</summary>
    public static Clip[] OfEveryFormat() =>
    [
        Text("plain text \u0645\u0631\u062D\u0628\u0627", source: "notepad.exe"),
        Html("<b>bold</b>", "bold", FakeTimeProvider.Start.AddSeconds(1)),
        Rtf("{\\rtf1 rich}", "rich", FakeTimeProvider.Start.AddSeconds(2)),
        Image([0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF], FakeTimeProvider.Start.AddSeconds(3)),
        Files(FakeTimeProvider.Start.AddSeconds(4), @"C:\one.txt", @"C:\two.txt"),
    ];
}
