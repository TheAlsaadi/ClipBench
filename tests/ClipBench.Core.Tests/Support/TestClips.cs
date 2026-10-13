using ClipBench.Core.Clips;

namespace ClipBench.Core.Tests.Support;

/// <summary>Builds clips for tests with sensible defaults.</summary>
internal static class TestClips
{
    /// <summary>The time the fake clock starts at, and the default capture time.</summary>
    public static readonly DateTimeOffset Start = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    public static Clip Text(string text, DateTimeOffset? capturedAt = null, string? source = null, bool pinned = false) =>
        new(ClipId.New(), new TextContent(text), capturedAt ?? Start, source, pinned);

    public static Clip Image(byte seed, DateTimeOffset? capturedAt = null) =>
        new(ClipId.New(), new ImageContent(new byte[] { 0x89, 0x50, seed }), capturedAt ?? Start);

    public static Clip Files(DateTimeOffset? capturedAt = null, params string[] paths) =>
        new(ClipId.New(), new FilesContent(paths), capturedAt ?? Start);
}
