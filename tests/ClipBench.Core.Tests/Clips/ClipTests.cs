using ClipBench.Core.Clips;

namespace ClipBench.Core.Tests.Clips;

public class ClipTests
{
    private static readonly DateTimeOffset _capturedAt = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly ClipId _id = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public void A_clip_keeps_what_it_was_given_and_is_not_pinned_by_default()
    {
        var clip = new Clip(_id, new TextContent("hello"), _capturedAt, "notepad.exe");

        clip.Id.ShouldBe(_id);
        clip.Content.ShouldBe(new TextContent("hello"));
        clip.CapturedAt.ShouldBe(_capturedAt);
        clip.SourceApp.ShouldBe("notepad.exe");
        clip.IsPinned.ShouldBeFalse();
    }

    [Fact]
    public void The_source_app_is_optional()
    {
        new Clip(_id, new TextContent("hello"), _capturedAt).SourceApp.ShouldBeNull();
    }

    [Fact]
    public void Clips_with_the_same_members_are_equal()
    {
        var first = new Clip(_id, new TextContent("hello"), _capturedAt, "notepad.exe");
        var second = new Clip(_id, new TextContent("hello"), _capturedAt, "notepad.exe");

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Clips_differ_when_any_member_differs()
    {
        var clip = new Clip(_id, new TextContent("hello"), _capturedAt, "notepad.exe");

        clip.ShouldNotBe(clip with { Id = ClipId.New() });
        clip.ShouldNotBe(clip with { Content = new TextContent("other") });
        clip.ShouldNotBe(clip with { CapturedAt = _capturedAt.AddSeconds(1) });
        clip.ShouldNotBe(clip with { SourceApp = "code.exe" });
        clip.ShouldNotBe(clip with { IsPinned = true });
    }

    [Fact]
    public void Clips_with_image_content_are_equal_when_the_bytes_match()
    {
        var first = new Clip(_id, new ImageContent(new byte[] { 1, 2, 3 }), _capturedAt);
        var second = new Clip(_id, new ImageContent(new byte[] { 1, 2, 3 }), _capturedAt);

        first.ShouldBe(second);
    }

    [Fact]
    public void A_clip_without_content_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new Clip(_id, null!, _capturedAt));
        var clip = new Clip(_id, new TextContent("x"), _capturedAt);
        Should.Throw<ArgumentNullException>(() => clip with { Content = null! });
    }

    [Fact]
    public void The_size_of_a_clip_is_the_size_of_its_content()
    {
        new Clip(_id, new TextContent("héllo"), _capturedAt).SizeInBytes.ShouldBe(6);
    }

    [Fact]
    public void Printing_a_clip_or_its_content_never_reveals_the_content_or_the_source_app()
    {
        // Clips end up in log calls and test output by accident. Content and source are protected data.
        var clip = new Clip(_id, new TextContent("secret-marker"), _capturedAt, "source-marker.exe");

        clip.ToString().ShouldNotContain("secret-marker");
        clip.ToString().ShouldNotContain("source-marker");
        clip.Content.ToString().ShouldNotContain("secret-marker");
        new HtmlContent("<b>secret-marker</b>", "secret-marker").ToString().ShouldNotContain("secret-marker");
        new RtfContent("secret-marker", "secret-marker").ToString().ShouldNotContain("secret-marker");
        new FilesContent([@"C:\secret-marker.txt"]).ToString().ShouldNotContain("secret-marker");
        new ImageContent(new byte[] { 1, 2, 3 }).ToString().ShouldNotContain("1, 2, 3");
    }

    [Fact]
    public void New_ids_are_unique_and_not_empty()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => ClipId.New()).ToHashSet();

        ids.Count.ShouldBe(1000);
        ids.Contains(default).ShouldBeFalse();
    }
}
