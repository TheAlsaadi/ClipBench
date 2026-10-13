using ClipBench.Core.History;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests.Clips;

public sealed class ClipRepositoryWipeTests : RepositoryTestBase
{
    // The first bytes of a sealed value are the random nonce and the start of the ciphertext, which are
    // unique to that value. If they are found on disk, the value is still there.
    private static byte[] Fingerprint(byte[] sealedValue) => sealedValue[..32];

    private byte[][] FilesOnDisk() =>
    [
        .. new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm" }
            .Where(File.Exists)
            .Select(path =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }),
    ];

    private bool AppearsOnDisk(byte[] fingerprint) => FilesOnDisk().Any(file => file.AsSpan().IndexOf(fingerprint) >= 0);

    [Fact]
    public async Task Wiping_removes_every_clip_including_pinned_ones_and_the_repository_still_works()
    {
        var clips = TestClips.OfEveryFormat();
        await using var repository = await OpenAsync();
        await SaveAll(repository, clips);
        await repository.SetPinnedAsync(clips[0].Id, true, Ct);

        (await repository.WipeAsync(Ct)).IsSuccess.ShouldBeTrue();

        RawCount("SELECT COUNT(*) FROM clips").ShouldBe(0);
        var fresh = TestClips.Text("after the wipe");
        await SaveAll(repository, fresh);
        (await repository.GetAsync(fresh.Id, Ct)).Value.ShouldBe(fresh);
    }

    [Fact]
    public async Task After_a_wipe_the_database_still_opens_with_the_same_key()
    {
        await using (var first = await OpenAsync())
        {
            await SaveAll(first, TestClips.Text("to be wiped"));
            await first.WipeAsync(Ct);
        }

        await using var second = await OpenAsync();

        (await second.LoadAllAsync(Ct)).Value.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Wiping_leaves_none_of_the_removed_values_on_disk()
    {
        var clip = TestClips.Text("wipe this value completely", source: "wiper.exe");
        await using var repository = await OpenAsync();
        await SaveAll(repository, clip, TestClips.Text("and a neighbour that stays in the same pages"));
        var content = Fingerprint(RawBlob("content", clip.Id));
        var source = Fingerprint(RawBlob("source_app", clip.Id));
        var preview = Fingerprint(RawBlob("preview", clip.Id));

        await repository.WipeAsync(Ct);

        AppearsOnDisk(content).ShouldBeFalse();
        AppearsOnDisk(source).ShouldBeFalse();
        AppearsOnDisk(preview).ShouldBeFalse();
    }

    [Fact]
    public async Task Wiping_leaves_none_of_the_removed_values_on_disk_after_the_database_is_closed()
    {
        var clip = TestClips.Text("wipe and close");
        byte[] content;
        await using (var repository = await OpenAsync())
        {
            await SaveAll(repository, clip);
            content = Fingerprint(RawBlob("content", clip.Id));
            await repository.WipeAsync(Ct);
        }

        AppearsOnDisk(content).ShouldBeFalse();
    }

    [Fact]
    public async Task Wiping_rebuilds_the_database_so_the_file_shrinks()
    {
        var big = TestClips.Image(System.Security.Cryptography.RandomNumberGenerator.GetBytes(2 * 1024 * 1024));
        await using var repository = await OpenAsync();
        await SaveAll(repository, big);
        await repository.WipeAsync(Ct);

        // Without a rebuild the emptied pages stay in the file, and it stays about as large as it was.
        new FileInfo(DatabasePath).Length.ShouldBeLessThan(200 * 1024);
        new FileInfo(DatabasePath + "-wal").Length.ShouldBe(0, "the write-ahead log is emptied");
    }

    [Fact]
    public async Task The_scan_can_see_a_value_that_has_not_been_wiped()
    {
        // The control for the tests above: without it, "not found" could mean the scan is blind.
        var clip = TestClips.Text("still here");
        await using var repository = await OpenAsync();
        await SaveAll(repository, clip);
        var content = Fingerprint(RawBlob("content", clip.Id));

        AppearsOnDisk(content).ShouldBeTrue();
    }

    [Fact]
    public async Task Deleting_a_clip_overwrites_its_value_on_disk()
    {
        // Needs secure_delete: a plain delete only marks the space free and leaves the value in the page.
        var clip = TestClips.Text("delete me for real");
        await using var repository = await OpenAsync();
        await SaveAll(repository, clip, TestClips.Text("keeps the page in use"));
        var content = Fingerprint(RawBlob("content", clip.Id));

        await repository.DeleteAsync([clip.Id], Ct);
        await repository.DisposeAsync();

        AppearsOnDisk(content).ShouldBeFalse();
    }

    [Fact]
    public async Task Deleting_a_clip_overwrites_its_value_on_disk_even_while_the_database_is_open()
    {
        // Closing the database empties its log, which would hide a value still sitting in it.
        var clip = TestClips.Text("delete me while running");
        await using var repository = await OpenAsync();
        await SaveAll(repository, clip, TestClips.Text("keeps the page in use"));
        var content = Fingerprint(RawBlob("content", clip.Id));
        AppearsOnDisk(content).ShouldBeTrue("control: the value is on disk before the delete");

        await repository.DeleteAsync([clip.Id], Ct);

        AppearsOnDisk(content).ShouldBeFalse();
    }

    [Fact]
    public async Task Retention_that_evicts_a_clip_overwrites_its_value_on_disk_even_while_the_database_is_open()
    {
        var old = TestClips.Text("evicted while running", FakeTimeProvider.Start.AddDays(-90));
        await using var repository = await OpenAsync();
        await SaveAll(repository, old, TestClips.Text("recent"));
        var content = Fingerprint(RawBlob("content", old.Id));

        await repository.RetainAsync(new HistoryOptions(), Ct);

        AppearsOnDisk(content).ShouldBeFalse();
    }

    [Fact]
    public async Task Sweeping_an_expired_clip_overwrites_its_value_on_disk_even_while_the_database_is_open()
    {
        var expiring = TestClips.Text("expires while running");
        await using var repository = await OpenAsync();
        await repository.SaveAsync(expiring, FakeTimeProvider.Start.AddMinutes(1), Ct);
        await SaveAll(repository, TestClips.Text("stays"));
        var content = Fingerprint(RawBlob("content", expiring.Id));
        Clock.Advance(TimeSpan.FromMinutes(2));

        await repository.SweepExpiredAsync(Ct);

        AppearsOnDisk(content).ShouldBeFalse();
    }

    [Fact]
    public async Task Retention_that_evicts_a_clip_overwrites_its_value_on_disk()
    {
        var old = TestClips.Text("evicted by age", FakeTimeProvider.Start.AddDays(-90));
        await using var repository = await OpenAsync();
        await SaveAll(repository, old, TestClips.Text("recent"));
        var content = Fingerprint(RawBlob("content", old.Id));

        await repository.RetainAsync(new HistoryOptions(), Ct);
        await repository.DisposeAsync();

        AppearsOnDisk(content).ShouldBeFalse();
    }
}
