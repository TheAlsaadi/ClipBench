using ClipBench.Core.Results;
using ClipBench.Core.Security;
using ClipBench.Storage.Keys;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests.Keys;

public sealed class MasterKeyStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly FakeKeyProtector _protector = new();

    public void Dispose() => _dir.Dispose();

    private MasterKeyStore Store(string name = "key.bin", IKeyProtector? protector = null) =>
        new(_dir.File(name), protector ?? _protector);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string KeyFile => _dir.File("key.bin");

    private static async Task<MasterKey> Open(MasterKeyStore store)
    {
        var result = await store.LoadOrCreateAsync(Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    private static async Task<ResultError> OpenFails(MasterKeyStore store)
    {
        var result = await store.LoadOrCreateAsync(Ct);
        result.IsFailure.ShouldBeTrue();
        return result.Error;
    }

    // --- first run and second run ---

    [Fact]
    public async Task The_first_run_creates_a_key_and_stores_it_wrapped()
    {
        using var key = await Open(Store());

        key.Bytes.Length.ShouldBe(MasterKey.Size);
        File.Exists(KeyFile).ShouldBeTrue();
        _protector.ProtectCalls.ShouldBe(1);
    }

    [Fact]
    public async Task The_stored_file_does_not_contain_the_key()
    {
        using var key = await Open(Store());

        var stored = await File.ReadAllBytesAsync(KeyFile, Ct);

        stored.AsSpan().IndexOf(key.Bytes).ShouldBe(-1);
    }

    [Fact]
    public async Task The_second_run_unwraps_the_same_key()
    {
        using var first = await Open(Store());
        var expected = first.Bytes.ToArray();

        using var second = await Open(Store());

        second.Bytes.ToArray().ShouldBe(expected);
        _protector.ProtectCalls.ShouldBe(1, "the second run must not create another key");
    }

    [Fact]
    public async Task Different_folders_get_different_keys()
    {
        using var other = new TempDirectory();
        using var first = await Open(Store());
        using var second = await Open(new MasterKeyStore(other.File("key.bin"), _protector));

        first.Bytes.SequenceEqual(second.Bytes).ShouldBeFalse();
    }

    [Fact]
    public async Task A_missing_folder_is_created()
    {
        var nested = Path.Combine(_dir.Path, "a", "b", "key.bin");
        using var key = await Open(new MasterKeyStore(nested, _protector));

        File.Exists(nested).ShouldBeTrue();
    }

    [Fact]
    public async Task No_temporary_file_is_left_behind()
    {
        using var key = await Open(Store());

        Directory.GetFiles(_dir.Path).Select(Path.GetFileName).ShouldBe(["key.bin"]);
    }

    // --- damaged or foreign key files: a clear error, nothing overwritten ---

    [Fact]
    public async Task A_file_of_garbage_is_a_recoverable_error_and_is_left_untouched()
    {
        var garbage = Enumerable.Range(0, 200).Select(i => (byte)(i * 7)).ToArray();
        await File.WriteAllBytesAsync(KeyFile, garbage, Ct);

        var error = await OpenFails(Store());

        error.Code.ShouldBe(ErrorCode.HistoryUnreadable);
        (await File.ReadAllBytesAsync(KeyFile, Ct)).ShouldBe(garbage);
        _protector.ProtectCalls.ShouldBe(0, "a damaged key must never be replaced silently");
    }

    [Fact]
    public async Task An_empty_file_is_a_recoverable_error()
    {
        await File.WriteAllBytesAsync(KeyFile, [], Ct);

        (await OpenFails(Store())).Code.ShouldBe(ErrorCode.HistoryUnreadable);
        new FileInfo(KeyFile).Length.ShouldBe(0);
    }

    [Fact]
    public async Task A_truncated_key_file_is_a_recoverable_error()
    {
        using var key = await Open(Store());
        var whole = await File.ReadAllBytesAsync(KeyFile, Ct);
        var truncated = whole[..(whole.Length - 5)];
        await File.WriteAllBytesAsync(KeyFile, truncated, Ct);

        (await OpenFails(Store())).Code.ShouldBe(ErrorCode.HistoryUnreadable);
        (await File.ReadAllBytesAsync(KeyFile, Ct)).ShouldBe(truncated);
    }

    [Fact]
    public async Task A_key_file_with_the_wrong_header_is_a_recoverable_error()
    {
        using var key = await Open(Store());
        var bytes = await File.ReadAllBytesAsync(KeyFile, Ct);
        bytes[0] ^= 0xFF;
        await File.WriteAllBytesAsync(KeyFile, bytes, Ct);

        (await OpenFails(Store())).Code.ShouldBe(ErrorCode.HistoryUnreadable);
    }

    [Fact]
    public async Task A_huge_key_file_is_rejected_without_being_read_into_memory()
    {
        // It starts with a valid header, so only the size limit can stop it from being read and unwrapped.
        await using (var stream = new FileStream(KeyFile, FileMode.Create))
        {
            await stream.WriteAsync("CBK1"u8.ToArray(), Ct);
            stream.SetLength(50L * 1024 * 1024);
        }

        (await OpenFails(Store())).Code.ShouldBe(ErrorCode.HistoryUnreadable);
        _protector.UnprotectCalls.ShouldBe(0);
    }

    [Fact]
    public async Task A_key_wrapped_for_another_user_is_a_recoverable_error()
    {
        using var key = await Open(Store(protector: new FakeKeyProtector("bob")));

        var error = await OpenFails(Store(protector: new FakeKeyProtector("alice")));

        error.Code.ShouldBe(ErrorCode.HistoryUnreadable);
    }

    [Fact]
    public async Task A_key_that_unwraps_to_the_wrong_length_is_a_recoverable_error()
    {
        using var key = await Open(Store());
        _protector.UnprotectOutput = new byte[16];

        (await OpenFails(Store())).Code.ShouldBe(ErrorCode.HistoryUnreadable);
    }

    [Fact]
    public async Task The_error_message_offers_a_way_forward_and_shows_no_secrets()
    {
        await File.WriteAllBytesAsync(KeyFile, [1, 2, 3], Ct);

        var error = await OpenFails(Store());

        error.Message.ShouldContain("start");
        error.Message.ShouldNotContain(_dir.Path, Case.Insensitive);
    }

    // --- first run failures leave nothing behind ---

    [Fact]
    public async Task A_protector_that_cannot_wrap_fails_without_writing_a_file()
    {
        _protector.FailProtect = true;

        var error = await OpenFails(Store());

        error.Code.ShouldBe(ErrorCode.KeyProtectionFailed);
        Directory.GetFiles(_dir.Path).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_wrap_that_cannot_be_unwrapped_again_is_never_written()
    {
        // Writing it would orphan every clip saved under the key. The store checks first.
        _protector.ProtectOutput = [1, 2, 3, 4];

        var error = await OpenFails(Store());

        error.Code.ShouldBe(ErrorCode.KeyProtectionFailed);
        Directory.GetFiles(_dir.Path).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_wrap_that_unwraps_to_a_different_key_is_never_written()
    {
        _protector.UnprotectOutput = new byte[MasterKey.Size];

        var error = await OpenFails(Store());

        error.Code.ShouldBe(ErrorCode.KeyProtectionFailed);
        Directory.GetFiles(_dir.Path).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_key_file_that_appears_during_creation_wins_and_is_not_overwritten()
    {
        // Another process creates the key between our check and our move.
        var rival = new FakeKeyProtector();
        var rivalKey = TestKeys.Master(7);
        var rivalFile = Array.Empty<byte>();
        _protector.OnProtect = () =>
        {
            rivalFile = [.. "CBK1"u8, .. rival.Protect(rivalKey).Value];
            File.WriteAllBytes(KeyFile, rivalFile);
        };

        using var key = await Open(Store());

        key.Bytes.ToArray().ShouldBe(rivalKey);
        (await File.ReadAllBytesAsync(KeyFile, Ct)).ShouldBe(rivalFile);
        Directory.GetFiles(_dir.Path).Select(Path.GetFileName).ShouldBe(["key.bin"]);
    }

    // --- deleting the key ---

    [Fact]
    public async Task Deleting_the_key_removes_the_file()
    {
        using var key = await Open(Store());

        var result = await Store().DeleteAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        File.Exists(KeyFile).ShouldBeFalse();
    }

    [Fact]
    public async Task Deleting_a_key_that_does_not_exist_succeeds()
    {
        var result = await Store().DeleteAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task The_key_file_is_overwritten_before_it_is_deleted()
    {
        using var key = await Open(Store());
        var original = await File.ReadAllBytesAsync(KeyFile, Ct);

        // A reader that keeps the file open still sees its contents after the delete, so it shows what was
        // on disk at the moment of the delete.
        await using var reader = new FileStream(KeyFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        (await Store().DeleteAsync(Ct)).IsSuccess.ShouldBeTrue();
        var seen = new byte[original.Length];
        reader.Position = 0;
        await reader.ReadExactlyAsync(seen, Ct);

        File.Exists(KeyFile).ShouldBeFalse();
        seen.ShouldNotBe(original);
    }

    // --- cancellation and construction ---

    [Fact]
    public async Task A_cancelled_request_throws_and_creates_nothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => Store().LoadOrCreateAsync(cancelled.Token));
        Directory.GetFiles(_dir.Path).ShouldBeEmpty();
    }

    [Fact]
    public void The_store_needs_a_path_and_a_protector()
    {
        Should.Throw<ArgumentException>(() => new MasterKeyStore(string.Empty, _protector));
        Should.Throw<ArgumentNullException>(() => new MasterKeyStore(KeyFile, null!));
    }

    [Fact]
    public void The_store_reports_its_path()
    {
        Store().KeyFilePath.ShouldBe(KeyFile);
    }
}
