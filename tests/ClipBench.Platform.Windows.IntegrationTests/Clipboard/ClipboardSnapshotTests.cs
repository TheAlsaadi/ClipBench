using System.Text;
using ClipBench.Core.Capture;
using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.IntegrationTests.Support;

namespace ClipBench.Platform.Windows.IntegrationTests.Clipboard;

public class ClipboardSnapshotTests
{
    private const long Plenty = 1L << 30;

    private readonly FakeClipboardApi _api = new();

    private ClipboardSnapshot? Take(long maxBytes = Plenty)
    {
        using var session = _api.TryOpen()!;
        return ClipboardSnapshot.Take(session, maxBytes);
    }

    private bool Restore(ClipboardSnapshot snapshot)
    {
        using var session = _api.TryOpen()!;
        return snapshot.Restore(session);
    }

    private (uint Id, byte[] Data)[] Contents() => [.. _api.FormatIds.Select(id => (id, _api.DataOf(id)!))];

    [Fact]
    public void What_was_on_the_clipboard_comes_back_format_for_format_and_byte_for_byte()
    {
        _api.PutText("original");
        _api.PutHtml("<b>original</b>");
        _api.PutMarker(ClipboardFormatNames.ExcludeFromMonitorProcessing);
        _api.Put("Some Private Format", [9, 8, 7]);
        var before = Contents();
        using var snapshot = Take()!;

        _api.Clear();
        _api.PutText("what the paste put there");
        Restore(snapshot).ShouldBeTrue();

        Contents().Select(entry => entry.Id).ShouldBe(before.Select(entry => entry.Id));
        for (var i = 0; i < before.Length; i++)
        {
            Contents()[i].Data.ShouldBe(before[i].Data);
        }
    }

    [Fact]
    public void Privacy_markers_are_restored_too_so_a_password_stays_excluded_from_history()
    {
        _api.PutText("a password");
        _api.PutMarker(ClipboardFormatNames.ExcludeFromMonitorProcessing);
        using var snapshot = Take()!;
        _api.Clear();

        Restore(snapshot);

        _api.Has(ClipboardFormatNames.ExcludeFromMonitorProcessing).ShouldBeTrue();
    }

    [Fact]
    public void A_restore_can_tag_the_clipboard_as_ClipBenchs_own_and_does_not_by_default()
    {
        _api.PutText("original");
        using var snapshot = Take()!;
        _api.Clear();

        using (var session = _api.TryOpen()!)
        {
            snapshot.Restore(session).ShouldBeTrue();
        }

        _api.Has(ClipboardFormatNames.OwnWrite).ShouldBeFalse();

        _api.Clear();
        using (var session = _api.TryOpen()!)
        {
            snapshot.Restore(session, _api.IdOf(ClipboardFormatNames.OwnWrite)).ShouldBeTrue();
        }

        _api.Has(ClipboardFormatNames.OwnWrite).ShouldBeTrue();
        _api.DataOf(ClipboardFormats.UnicodeText).ShouldBe(Encoding.Unicode.GetBytes("original\0"));
    }

    [Fact]
    public void A_tag_that_Windows_refuses_does_not_make_the_restore_incomplete()
    {
        _api.PutText("original");
        using var snapshot = Take()!;
        _api.Clear();
        var marker = _api.IdOf(ClipboardFormatNames.OwnWrite);
        _api.RefusedFormats.Add(marker);

        using var session = _api.TryOpen()!;

        snapshot.Restore(session, marker).ShouldBeTrue();
    }

    [Fact]
    public void An_empty_clipboard_is_restored_as_an_empty_clipboard()
    {
        using var snapshot = Take()!;
        _api.PutText("the paste");

        Restore(snapshot).ShouldBeTrue();

        _api.FormatIds.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(2u)]       // CF_BITMAP: a handle, not memory
    [InlineData(3u)]       // CF_METAFILEPICT
    [InlineData(9u)]       // CF_PALETTE
    [InlineData(14u)]      // CF_ENHMETAFILE
    [InlineData(0x82u)]    // CF_DSPBITMAP
    [InlineData(0x205u)]   // inside the private range
    [InlineData(0x305u)]   // inside the GDI object range
    public void Formats_that_are_handles_and_not_memory_are_left_out(uint format)
    {
        _api.PutText("keep");
        _api.Put(format, [1, 2, 3]);
        using var snapshot = Take()!;
        _api.Clear();

        Restore(snapshot);

        _api.FormatIds.ShouldBe([ClipboardFormats.UnicodeText]);
    }

    [Fact]
    public void A_clipboard_bigger_than_the_cap_is_not_snapshotted_and_nothing_is_kept()
    {
        _api.PutText(new string('x', 100));   // 202 bytes
        _api.Put("Another", new byte[100]);

        Take(maxBytes: 250).ShouldBeNull();
    }

    [Fact]
    public void A_clipboard_exactly_at_the_cap_is_snapshotted()
    {
        _api.Put("Only", new byte[100]);

        Take(maxBytes: 100).ShouldNotBeNull();
    }

    [Fact]
    public void A_format_that_cannot_be_read_is_skipped_and_the_rest_are_kept()
    {
        _api.PutText("readable");
        _api.PutHtml("<i>unreadable</i>");
        _api.UnreadableFormats.Add(_api.IdOf(ClipboardFormats.Html));
        using var snapshot = Take()!;
        _api.Clear();

        Restore(snapshot);

        _api.FormatIds.ShouldBe([ClipboardFormats.UnicodeText]);
    }

    [Fact]
    public void If_Windows_will_not_empty_the_clipboard_restoring_fails_and_changes_nothing()
    {
        _api.PutText("original");
        using var snapshot = Take()!;
        _api.Clear();
        _api.PutText("current");
        _api.RefuseEmpty = true;

        Restore(snapshot).ShouldBeFalse();

        _api.DataOf(ClipboardFormats.UnicodeText).ShouldBe(Encoding.Unicode.GetBytes("current\0"));
    }

    [Fact]
    public void If_a_format_is_refused_the_others_are_still_restored_and_the_result_says_it_was_not_complete()
    {
        _api.PutText("original");
        _api.PutHtml("<b>original</b>");
        using var snapshot = Take()!;
        _api.Clear();
        _api.RefusedFormats.Add(ClipboardFormats.UnicodeText);

        Restore(snapshot).ShouldBeFalse();

        _api.Has(ClipboardFormats.Html).ShouldBeTrue();
    }

    [Fact]
    public void Disposing_a_snapshot_overwrites_what_it_held()
    {
        _api.PutText("a secret");
        var snapshot = Take()!;
        var buffers = _api.HandedOut;
        buffers.ShouldNotBeEmpty();
        buffers.All(buffer => buffer.Any(value => value != 0)).ShouldBeTrue("the copies hold the secret before disposal");

        snapshot.Dispose();

        buffers.All(buffer => buffer.All(value => value == 0)).ShouldBeTrue();
    }
}
