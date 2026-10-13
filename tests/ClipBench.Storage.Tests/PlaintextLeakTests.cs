using System.Text;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests;

/// <summary>
/// Security baseline S4. Everything that reveals what was copied must be unreadable on disk. Each test saves
/// clips whose text contains markers, then reads every byte of every file in the storage folder (the database,
/// its write-ahead log, its shared-memory file and the key file) looking for each marker in both UTF-8 and
/// UTF-16, the two ways .NET holds text. The scan is checked against a known leak first, so "nothing found"
/// cannot mean "the scan is blind".
/// </summary>
public sealed class PlaintextLeakTests : IAsyncDisposable
{
    private const string Text = "LEAKTEXT-5d8e1c7a";
    private const string Html = "LEAKHTML-3b9a60f2";
    private const string HtmlText = "LEAKHTMLTEXT-77c1e4d8";
    private const string Rtf = "LEAKRTF-a41f08be";
    private const string RtfText = "LEAKRTFTEXT-62d93c15";
    private const string FilePath = "LEAKFILE-0e7b52a9";
    private const string Image = "LEAKIMAGE-9c3d17f4";
    private const string Source = "LEAKSOURCE-18fa6b2d";
    private const string Arabic = "\u0633\u0631\u0651-\u0645\u0643\u0634\u0648\u0641-\u0661\u0662\u0663";

    private static readonly string[] _markers = [Text, Html, HtmlText, Rtf, RtfText, FilePath, Image, Source, Arabic];

    private static readonly string[] _filesWhileOpen = ["clipbench.db", "clipbench.db-shm", "clipbench.db-wal", "key.bin"];

    private readonly TempDirectory _directory = new();
    private readonly FakeKeyProtector _protector = new();
    private readonly FakeTimeProvider _clock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private StoragePaths Paths => new(_directory.Path);

    public ValueTask DisposeAsync()
    {
        _directory.Dispose();
        return ValueTask.CompletedTask;
    }

    private static Clip[] ClipsWithMarkers() =>
    [
        TestClips.Text($"copied: {Text}", source: $"{Source}.exe"),
        TestClips.Html($"<p>{Html}</p>", HtmlText),
        TestClips.Rtf($"{{\\rtf1 {Rtf}}}", RtfText),
        TestClips.Files(null, $@"C:\Users\{FilePath}\secret.docx"),
        TestClips.Image([.. new byte[] { 0x89, 0x50, 0x4E, 0x47 }, .. Encoding.ASCII.GetBytes(Image), 0x00, 0xFF]),
        TestClips.Text($"arabic: {Arabic}"),
        TestClips.Text($"pinned {Text} again", pinned: true),
    ];

    private async Task<ClipStorage> OpenWithClipsAsync()
    {
        var opened = await ClipStorage.OpenAsync(Paths, _protector, _clock, Ct);
        opened.IsSuccess.ShouldBeTrue();
        foreach (var clip in ClipsWithMarkers())
        {
            (await opened.Value.Repository.SaveAsync(clip, Ct)).IsSuccess.ShouldBeTrue();
        }

        return opened.Value;
    }

    private async Task<ClipStorage> ReopenAsync()
    {
        var opened = await ClipStorage.OpenAsync(Paths, _protector, _clock, Ct);
        opened.IsSuccess.ShouldBeTrue();
        return opened.Value;
    }

    private void AssertNoMarkerOnDisk()
    {
        foreach (var marker in _markers)
        {
            DiskScan.FilesContaining(_directory.Path, marker).ShouldBeEmpty($"'{marker}' is readable on disk");
        }
    }

    // --- the scan itself ---

    [Fact]
    public void The_scan_finds_a_marker_in_either_encoding_when_one_is_there()
    {
        File.WriteAllBytes(Path.Combine(_directory.Path, "utf8.bin"), [0, 0, .. Encoding.UTF8.GetBytes(Text), 0]);
        File.WriteAllBytes(Path.Combine(_directory.Path, "utf16.bin"), [0, .. Encoding.Unicode.GetBytes(Arabic), 0]);

        DiskScan.FilesContaining(_directory.Path, Text).ShouldBe(["utf8.bin"]);
        DiskScan.FilesContaining(_directory.Path, Arabic).ShouldBe(["utf16.bin"]);
        DiskScan.FilesContaining(_directory.Path, "not there").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_scan_can_see_the_plain_columns_so_it_is_not_blind_to_the_database()
    {
        await using var storage = await OpenWithClipsAsync();

        // The schema and the column names are stored in plain text, as designed. If the scan cannot see them,
        // its silence about the markers would mean nothing.
        DiskScan.FilesContaining(_directory.Path, "captured_at").ShouldNotBeEmpty();
    }

    // --- nothing readable, at every point in the life of the database ---

    [Fact]
    public async Task Nothing_readable_is_on_disk_while_the_database_is_open()
    {
        await using var storage = await OpenWithClipsAsync();

        AssertNoMarkerOnDisk();
        Directory.GetFiles(_directory.Path).Select(Path.GetFileName).ShouldContain("key.bin");
    }

    [Fact]
    public async Task Nothing_readable_is_on_disk_after_the_database_is_closed()
    {
        var storage = await OpenWithClipsAsync();
        await storage.DisposeAsync();

        AssertNoMarkerOnDisk();
    }

    [Fact]
    public async Task Nothing_readable_is_on_disk_after_a_restart_and_more_changes()
    {
        var first = await OpenWithClipsAsync();
        await first.DisposeAsync();

        await using var second = await ReopenAsync();
        var loaded = (await second.Repository.LoadAllAsync(Ct)).Value.Items;
        await second.Repository.SetPinnedAsync(loaded[0].Id, false, Ct);
        await second.Repository.DeleteAsync([loaded[1].Id], Ct);
        await second.Repository.RetainAsync(new HistoryOptions { MaxCount = 2 }, Ct);
        await second.Repository.SaveAsync(TestClips.Text($"a new one with {Text}"), Ct);

        AssertNoMarkerOnDisk();
    }

    [Fact]
    public async Task Nothing_readable_is_on_disk_while_a_second_reader_has_the_database_open()
    {
        await using var storage = await OpenWithClipsAsync();
        await using var reader = await ReopenAsync();

        (await reader.Repository.LoadAllAsync(Ct)).Value.Items.Length.ShouldBe(ClipsWithMarkers().Length);
        AssertNoMarkerOnDisk();
    }

    [Fact]
    public async Task The_key_file_holds_the_wrapped_key_and_no_clip_text()
    {
        await using var storage = await OpenWithClipsAsync();

        var key = await File.ReadAllBytesAsync(Paths.KeyPath, Ct);

        key.Length.ShouldBeLessThan(512);
        foreach (var marker in _markers)
        {
            key.AsSpan().IndexOf(Encoding.UTF8.GetBytes(marker)).ShouldBe(-1, $"'{marker}' is in the key file");
        }
    }

    [Fact]
    public async Task No_other_file_is_created_beside_the_database_the_log_and_the_key()
    {
        await using var storage = await OpenWithClipsAsync();

        Directory.GetFiles(_directory.Path).Select(Path.GetFileName).Order().ShouldBe(_filesWhileOpen);
    }

    [Fact]
    public async Task Only_the_database_files_and_the_key_remain_after_closing()
    {
        var storage = await OpenWithClipsAsync();
        await storage.DisposeAsync();

        Directory.GetFiles(_directory.Path).Select(Path.GetFileName).Order().ShouldBe(["clipbench.db", "key.bin"]);
    }

    // --- the same promise for the printed forms ---

    [Fact]
    public async Task Printing_a_clip_or_a_result_never_shows_the_text()
    {
        await using var storage = await OpenWithClipsAsync();
        var loaded = (await storage.Repository.LoadAllAsync(Ct)).Value.Items;
        var summaries = (await storage.Repository.LoadSummariesAsync(Ct)).Value.Items;

        var printed = string.Join(
            "\n",
            loaded.Select(clip => clip.ToString())
                .Concat(loaded.Select(clip => clip.Content.ToString()))
                .Concat(summaries.Select(summary => summary.ToString()))
                .Append((await storage.Repository.LoadAllAsync(Ct)).ToString()));

        _markers.ShouldAllBe(marker => !printed.Contains(marker, StringComparison.Ordinal));
    }
}
