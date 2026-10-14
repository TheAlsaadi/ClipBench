using System.Buffers.Binary;
using ClipBench.Core.Clips;
using ClipBench.Core.Privacy;
using ClipBench.Presentation.Rows;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Rows;

public class ClipRowViewModelTests
{
    private static readonly DateTimeOffset _now = TestClips.Start.AddMinutes(5);

    private static ClipRowViewModel Row(Clip clip, SensitiveKind sensitivity = SensitiveKind.None) => new(clip, sensitivity, _now);

    private static string[] ChangedProperties(ClipRowViewModel row, Action act)
    {
        List<string> names = [];
        row.PropertyChanged += (_, args) => names.Add(args.PropertyName!);
        act();
        return [.. names];
    }

    // --- text ---

    [Fact]
    public void A_text_row_shows_its_preview_its_source_and_how_long_ago_it_was_copied()
    {
        var clip = TestClips.Text("Meeting notes for Tuesday", source: "notepad.exe");

        var row = Row(clip);

        row.Id.ShouldBe(clip.Id);
        row.Format.ShouldBe(ClipFormat.Text);
        row.DisplayText.ShouldBe("Meeting notes for Tuesday");
        row.Details.ShouldBe("Notepad \u00B7 5 min ago");
        row.HasImage.ShouldBeFalse();
        row.ImagePng.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void A_row_without_a_known_source_shows_only_the_age()
    {
        Row(TestClips.Text("anonymous")).Details.ShouldBe("5 min ago");
    }

    [Fact]
    public void A_long_text_is_cut_to_a_couple_of_lines()
    {
        var row = Row(TestClips.Text("one\ntwo\nthree\nfour"));

        row.DisplayText.ShouldBe("one\ntwo\n\u2026");
    }

    // --- pins and marks ---

    [Fact]
    public void A_pinned_clip_makes_a_pinned_row()
    {
        Row(TestClips.Text("keep", pinned: true)).IsPinned.ShouldBeTrue();
        Row(TestClips.Text("let go")).IsPinned.ShouldBeFalse();
    }

    [Fact]
    public void Marking_a_row_is_announced()
    {
        var row = Row(TestClips.Text("mark me"));

        var changed = ChangedProperties(row, () => row.IsMarked = true);

        row.IsMarked.ShouldBeTrue();
        changed.ShouldContain(nameof(ClipRowViewModel.IsMarked));
    }

    // --- sensitive clips ---

    [Fact]
    public void A_sensitive_clip_is_hidden_until_it_is_revealed()
    {
        var row = Row(TestClips.Text("4111 1111 1111 1111"), SensitiveKind.CardNumber);

        row.IsSensitive.ShouldBeTrue();
        row.DisplayText.ShouldBe(ClipRowViewModel.Mask);

        var changed = ChangedProperties(row, () => row.IsRevealed = true);

        row.DisplayText.ShouldBe("4111 1111 1111 1111");
        changed.ShouldContain(nameof(ClipRowViewModel.DisplayText));
        changed.ShouldContain(nameof(ClipRowViewModel.AutomationName));

        row.IsRevealed = false;
        row.DisplayText.ShouldBe(ClipRowViewModel.Mask);
    }

    [Fact]
    public void The_mask_does_not_reveal_how_long_the_secret_is()
    {
        var short1 = Row(TestClips.Text("short"), SensitiveKind.Token);
        var long1 = Row(TestClips.Text(new string('x', 500)), SensitiveKind.Token);

        short1.DisplayText.ShouldBe(long1.DisplayText);
    }

    [Fact]
    public void An_ordinary_clip_is_not_hidden_and_revealing_it_changes_nothing()
    {
        var row = Row(TestClips.Text("lunch at noon"));

        row.IsSensitive.ShouldBeFalse();
        row.DisplayText.ShouldBe("lunch at noon");
    }

    // --- images ---

    [Fact]
    public void A_picture_row_carries_the_picture_and_its_size()
    {
        var png = ValidPngHeader(1280, 720);
        var row = Row(new Clip(ClipId.New(), new ImageContent(png.AsSpan()), TestClips.Start, "snippingtool.exe"));

        row.HasImage.ShouldBeTrue();
        row.ImagePng.ShouldBe([.. png]);
        row.DisplayText.ShouldBe("Image 1280 \u00D7 720");
        row.Details.ShouldBe("Snippingtool \u00B7 5 min ago");
    }

    // --- what a screen reader says ---

    [Fact]
    public void The_spoken_name_says_the_kind_the_text_the_source_the_age_and_the_pin()
    {
        var row = Row(TestClips.Text("Meeting notes", source: "notepad.exe", pinned: true));

        row.AutomationName.ShouldBe("Text, Meeting notes, from Notepad, 5 min ago, pinned");
    }

    [Fact]
    public void The_spoken_name_of_a_hidden_clip_does_not_read_it_out()
    {
        var row = Row(TestClips.Text("my-secret-token-123"), SensitiveKind.Token);

        row.AutomationName.ShouldBe("Text, hidden sensitive content, 5 min ago");
        row.AutomationName.ShouldNotContain("secret");
    }

    [Fact]
    public void A_revealed_clip_is_read_out_and_a_marked_one_says_so()
    {
        var row = Row(TestClips.Text("shown", source: "code.exe"), SensitiveKind.Token);
        row.IsRevealed = true;
        row.IsMarked = true;

        row.AutomationName.ShouldBe("Text, shown, from Code, 5 min ago, selected");
    }

    [Theory]
    [InlineData(ClipFormat.Html, "HTML")]
    [InlineData(ClipFormat.Rtf, "Rich text")]
    [InlineData(ClipFormat.Files, "Files")]
    [InlineData(ClipFormat.Image, "Image")]
    public void Every_kind_has_a_spoken_name(ClipFormat format, string spoken)
    {
        Clip clip = format switch
        {
            ClipFormat.Html => new Clip(ClipId.New(), new HtmlContent("<b>x</b>", "x"), TestClips.Start),
            ClipFormat.Rtf => new Clip(ClipId.New(), new RtfContent("{\\rtf1 x}", "x"), TestClips.Start),
            ClipFormat.Files => new Clip(ClipId.New(), new FilesContent([@"C:\a.txt"]), TestClips.Start),
            _ => new Clip(ClipId.New(), new ImageContent(new byte[] { 1 }), TestClips.Start),
        };

        Row(clip).AutomationName.ShouldStartWith(spoken + ", ");
    }

    private static System.Collections.Immutable.ImmutableArray<byte> ValidPngHeader(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        return [.. bytes];
    }
}
