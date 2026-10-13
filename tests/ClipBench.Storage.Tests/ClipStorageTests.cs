using ClipBench.Core.Clips;
using ClipBench.Core.Results;
using ClipBench.Core.Security;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests;

public sealed class ClipStorageTests : IAsyncDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly FakeKeyProtector _protector = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly List<ClipStorage> _opened = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private StoragePaths Paths => new(_directory.Path);

    public async ValueTask DisposeAsync()
    {
        foreach (var storage in _opened)
        {
            await storage.DisposeAsync();
        }

        _directory.Dispose();
    }

    private async Task<ClipStorage> OpenAsync(IKeyProtector? protector = null)
    {
        var result = await ClipStorage.OpenAsync(Paths, protector ?? _protector, _clock, Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? $"{result.Error.Code}: {result.Error.Message}" : string.Empty);
        _opened.Add(result.Value);
        return result.Value;
    }

    private async Task<Result<ClipStorage>> TryOpenAsync(IKeyProtector? protector = null)
    {
        var result = await ClipStorage.OpenAsync(Paths, protector ?? _protector, _clock, Ct);
        if (result.IsSuccess)
        {
            _opened.Add(result.Value);
        }

        return result;
    }

    private static async Task SaveAsync(ClipStorage storage, Clip clip) =>
        (await storage.Repository.SaveAsync(clip, Ct)).IsSuccess.ShouldBeTrue();

    // --- first run and restarts ---

    [Fact]
    public async Task The_first_run_creates_the_database_and_a_wrapped_key()
    {
        await OpenAsync();

        File.Exists(Paths.DatabasePath).ShouldBeTrue();
        File.Exists(Paths.KeyPath).ShouldBeTrue();
    }

    [Fact]
    public async Task History_survives_a_restart()
    {
        var clips = TestClips.OfEveryFormat();
        await using (var first = await OpenAsync())
        {
            foreach (var clip in clips)
            {
                await SaveAsync(first, clip);
            }
        }

        await using var second = await OpenAsync();
        var loaded = (await second.Repository.LoadAllAsync(Ct)).Value;

        loaded.UnreadableCount.ShouldBe(0);
        loaded.Items.ShouldBe(clips.Reverse());
    }

    [Fact]
    public async Task A_key_without_a_database_opens_a_new_database()
    {
        await using (var first = await OpenAsync())
        {
        }

        File.Delete(Paths.DatabasePath);

        await using var second = await OpenAsync();

        (await second.Repository.LoadAllAsync(Ct)).Value.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_database_file_with_no_key_is_a_first_run_not_a_lost_key()
    {
        // A crash during the very first start can leave an empty file behind. It holds no history to protect.
        await File.WriteAllBytesAsync(Paths.DatabasePath, [], Ct);

        await using var storage = await OpenAsync();

        File.Exists(Paths.KeyPath).ShouldBeTrue();
    }

    // --- a key that cannot be used: reported, nothing created or replaced ---

    [Fact]
    public async Task A_database_without_its_key_is_reported_and_no_new_key_is_made()
    {
        await using (var first = await OpenAsync())
        {
            await SaveAsync(first, TestClips.Text("needs its key"));
        }

        File.Delete(Paths.KeyPath);
        var databaseBefore = await File.ReadAllBytesAsync(Paths.DatabasePath, Ct);

        var result = await TryOpenAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.HistoryUnreadable);
        File.Exists(Paths.KeyPath).ShouldBeFalse("a new key would make the old history permanently unreadable");
        (await File.ReadAllBytesAsync(Paths.DatabasePath, Ct)).ShouldBe(databaseBefore);
    }

    [Fact]
    public async Task A_damaged_key_file_is_reported_and_left_alone()
    {
        await using (var first = await OpenAsync())
        {
            await SaveAsync(first, TestClips.Text("needs its key"));
        }

        var key = await File.ReadAllBytesAsync(Paths.KeyPath, Ct);
        key[^1] ^= 0xFF;
        key[^2] ^= 0xFF;
        await File.WriteAllBytesAsync(Paths.KeyPath, key, Ct);
        _protector.FailUnprotect = true;

        var result = await TryOpenAsync();

        result.Error.Code.ShouldBe(ErrorCode.HistoryUnreadable);
        (await File.ReadAllBytesAsync(Paths.KeyPath, Ct)).ShouldBe(key);
    }

    [Fact]
    public async Task A_key_that_belongs_to_another_user_is_reported()
    {
        await using (var first = await OpenAsync(new FakeKeyProtector("alice")))
        {
            await SaveAsync(first, TestClips.Text("alice's clip"));
        }

        var result = await TryOpenAsync(new FakeKeyProtector("bob"));

        result.Error.Code.ShouldBe(ErrorCode.HistoryUnreadable);
    }

    [Fact]
    public async Task A_database_that_is_not_a_database_is_reported()
    {
        await using (var first = await OpenAsync())
        {
        }

        await File.WriteAllBytesAsync(Paths.DatabasePath, Enumerable.Range(0, 3000).Select(i => (byte)i).ToArray(), Ct);

        (await TryOpenAsync()).Error.Code.ShouldBe(ErrorCode.HistoryUnreadable);
    }

    // --- starting fresh ---

    [Fact]
    public async Task Starting_fresh_after_a_damaged_key_gives_an_empty_working_history()
    {
        await using (var first = await OpenAsync())
        {
            await SaveAsync(first, TestClips.Text("lost for good"));
        }

        await File.WriteAllBytesAsync(Paths.KeyPath, [1, 2, 3], Ct);
        (await TryOpenAsync()).Error.Code.ShouldBe(ErrorCode.HistoryUnreadable);

        var fresh = await ClipStorage.StartFreshAsync(Paths, _protector, _clock, Ct);
        fresh.IsSuccess.ShouldBeTrue();
        _opened.Add(fresh.Value);
        (await fresh.Value.Repository.LoadAllAsync(Ct)).Value.Items.ShouldBeEmpty();
        var clip = TestClips.Text("saved after starting fresh");
        await SaveAsync(fresh.Value, clip);
        await fresh.Value.DisposeAsync();

        await using var reopened = await OpenAsync();
        (await reopened.Repository.GetAsync(clip.Id, Ct)).Value.ShouldBe(clip);
    }

    [Fact]
    public async Task Starting_fresh_removes_the_old_database_files()
    {
        await using (var first = await OpenAsync())
        {
            await SaveAsync(first, TestClips.Text("old"));
        }

        var fresh = await ClipStorage.StartFreshAsync(Paths, _protector, _clock, Ct);
        _opened.Add(fresh.Value);

        (await fresh.Value.Repository.LoadAllAsync(Ct)).Value.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Starting_fresh_on_a_folder_with_nothing_in_it_just_opens()
    {
        var fresh = await ClipStorage.StartFreshAsync(Paths, _protector, _clock, Ct);

        fresh.IsSuccess.ShouldBeTrue();
        _opened.Add(fresh.Value);
    }

    // --- wiping everything ---

    [Fact]
    public async Task Wiping_all_deletes_the_database_the_log_and_the_key()
    {
        var storage = await OpenAsync();
        await SaveAsync(storage, TestClips.Text("wipe me"));

        (await storage.WipeAllAsync(Ct)).IsSuccess.ShouldBeTrue();

        Directory.GetFiles(_directory.Path).ShouldBeEmpty();
    }

    [Fact]
    public async Task Wiping_all_scrubs_the_stored_values_before_the_files_are_deleted()
    {
        // Deleting a file leaves its bytes in the freed disk blocks, so the values must already be gone from
        // the files when they are deleted. Readers that hold the files open see them as they were at that moment.
        var clip = TestClips.Text("must not outlive the file");
        var storage = await OpenAsync();
        await SaveAsync(storage, clip);
        byte[] fingerprint;
        using (var raw = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Paths.DatabasePath};Pooling=False"))
        {
            raw.Open();
            using var command = raw.CreateCommand();
            command.CommandText = "SELECT content FROM clips;";
            fingerprint = ((byte[])command.ExecuteScalar()!)[..32];
        }

        var database = new FileStream(Paths.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var log = new FileStream(Paths.DatabasePath + "-wal", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        await using (database)
        await using (log)
        {
            (await storage.WipeAllAsync(Ct)).IsSuccess.ShouldBeTrue();

            database.Position = 0;
            log.Position = 0;
            var seen = new MemoryStream();
            await database.CopyToAsync(seen, Ct);
            await log.CopyToAsync(seen, Ct);

            seen.ToArray().AsSpan().IndexOf(fingerprint).ShouldBe(-1, "the value was still in the files when they were deleted");
        }
    }

    [Fact]
    public async Task The_storage_cannot_be_used_after_a_wipe_but_a_new_one_can_be_opened()
    {
        var storage = await OpenAsync();
        await storage.WipeAllAsync(Ct);

        await Should.ThrowAsync<ObjectDisposedException>(() => storage.Repository.LoadAllAsync(Ct));
        await using var again = await OpenAsync();
        (await again.Repository.LoadAllAsync(Ct)).Value.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_new_history_after_a_wipe_uses_a_new_key()
    {
        var storage = await OpenAsync();
        await storage.WipeAllAsync(Ct);

        await using var again = await OpenAsync();
        var newKey = await File.ReadAllBytesAsync(Paths.KeyPath, Ct);

        newKey.ShouldNotBeEmpty();
        _protector.ProtectCalls.ShouldBe(2, "one key before the wipe and a new one after it");
    }

    // --- lifetime ---

    [Fact]
    public async Task Disposing_twice_is_fine()
    {
        var storage = await OpenAsync();

        await storage.DisposeAsync();
        await storage.DisposeAsync();
    }

    [Fact]
    public async Task Open_needs_its_arguments()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => ClipStorage.OpenAsync(null!, _protector, _clock, Ct));
        await Should.ThrowAsync<ArgumentNullException>(() => ClipStorage.OpenAsync(Paths, null!, _clock, Ct));
        await Should.ThrowAsync<ArgumentNullException>(() => ClipStorage.OpenAsync(Paths, _protector, null!, Ct));
    }

    [Fact]
    public async Task A_cancelled_open_throws_and_creates_nothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => ClipStorage.OpenAsync(Paths, _protector, _clock, cancelled.Token));
        Directory.GetFiles(_directory.Path).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_open_closes_everything_it_opened()
    {
        await using (var first = await OpenAsync())
        {
        }

        await File.WriteAllBytesAsync(Paths.DatabasePath, Enumerable.Range(0, 3000).Select(i => (byte)i).ToArray(), Ct);
        (await TryOpenAsync()).IsFailure.ShouldBeTrue();

        // The files can be deleted only if nothing holds them open.
        File.Delete(Paths.DatabasePath);
        File.Delete(Paths.KeyPath);
    }
}
