using ClipBench.Core.Clips;
using ClipBench.Core.Results;
using ClipBench.Storage.Clips;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests.Clips;

/// <summary>
/// Someone with the database file but not the key can still edit it. Whatever they change must be detected
/// and must never produce a clip: a damaged record is skipped and counted, and the others still load.
/// </summary>
public sealed class ClipRepositoryTamperTests : RepositoryTestBase
{
    private readonly Clip _first = TestClips.Text("first clip", FakeTimeProvider.Start, "one.exe");
    private readonly Clip _second = TestClips.Text("second clip", FakeTimeProvider.Start.AddMinutes(1), "two.exe");
    private readonly Clip _third = TestClips.Text("third clip", FakeTimeProvider.Start.AddMinutes(2), "three.exe");

    private async Task<ClipRepository> OpenWithThreeAsync()
    {
        var repository = await OpenAsync();
        await SaveAll(repository, _first, _second, _third);
        return repository;
    }

    // --- changing a value ---

    [Theory]
    [InlineData("content")]
    [InlineData("source_app")]
    public async Task Changing_a_byte_of_a_sealed_column_makes_that_clip_unreadable_and_no_other(string column)
    {
        await using var repository = await OpenWithThreeAsync();
        var sealedValue = RawBlob(column, _second.Id);
        sealedValue[sealedValue.Length / 2] ^= 0x01;
        RawSet(column, _second.Id, sealedValue);

        var one = await repository.GetAsync(_second.Id, Ct);
        var all = (await repository.LoadAllAsync(Ct)).Value;

        one.IsFailure.ShouldBeTrue();
        one.Error.Code.ShouldBe(ErrorCode.DecryptionFailed);
        all.UnreadableCount.ShouldBe(1);
        all.Items.Select(clip => clip.Id).ShouldBe([_third.Id, _first.Id]);
    }

    [Fact]
    public async Task Changing_a_byte_of_the_preview_hides_that_clip_from_the_summaries_only()
    {
        await using var repository = await OpenWithThreeAsync();
        var sealedValue = RawBlob("preview", _second.Id);
        sealedValue[^1] ^= 0x80;
        RawSet("preview", _second.Id, sealedValue);

        var summaries = (await repository.LoadSummariesAsync(Ct)).Value;

        summaries.UnreadableCount.ShouldBe(1);
        summaries.Items.Select(summary => summary.Id).ShouldBe([_third.Id, _first.Id]);
        (await repository.GetAsync(_second.Id, Ct)).IsSuccess.ShouldBeTrue("the content itself is intact");
    }

    [Fact]
    public async Task A_summary_does_not_open_the_content()
    {
        await using var repository = await OpenWithThreeAsync();
        RawSet("content", _second.Id, new byte[] { 1, 2, 3 });

        var summaries = (await repository.LoadSummariesAsync(Ct)).Value;

        summaries.UnreadableCount.ShouldBe(0);
        summaries.Items.Length.ShouldBe(3);
    }

    [Fact]
    public async Task Emptying_a_sealed_column_is_detected()
    {
        await using var repository = await OpenWithThreeAsync();
        RawSet("content", _second.Id, Array.Empty<byte>());

        (await repository.GetAsync(_second.Id, Ct)).Error.Code.ShouldBe(ErrorCode.DecryptionFailed);
    }

    // --- moving a value to another place ---

    [Fact]
    public async Task Swapping_the_content_of_two_rows_makes_both_unreadable()
    {
        await using var repository = await OpenWithThreeAsync();
        RawSwap("content", _first.Id, _second.Id);

        (await repository.GetAsync(_first.Id, Ct)).Error.Code.ShouldBe(ErrorCode.DecryptionFailed);
        (await repository.GetAsync(_second.Id, Ct)).Error.Code.ShouldBe(ErrorCode.DecryptionFailed);
        (await repository.GetAsync(_third.Id, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Swapping_the_preview_of_two_rows_is_detected()
    {
        await using var repository = await OpenWithThreeAsync();
        RawSwap("preview", _first.Id, _second.Id);

        var summaries = (await repository.LoadSummariesAsync(Ct)).Value;

        summaries.UnreadableCount.ShouldBe(2);
        summaries.Items.Select(summary => summary.Id).ShouldBe([_third.Id]);
    }

    [Fact]
    public async Task Swapping_the_source_app_of_two_rows_is_detected()
    {
        await using var repository = await OpenWithThreeAsync();
        RawSwap("source_app", _first.Id, _second.Id);

        (await repository.GetAsync(_first.Id, Ct)).IsFailure.ShouldBeTrue();
        (await repository.GetAsync(_second.Id, Ct)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Putting_the_content_into_the_preview_column_is_detected()
    {
        await using var repository = await OpenWithThreeAsync();
        RawSet("preview", _second.Id, RawBlob("content", _second.Id));

        (await repository.LoadSummariesAsync(Ct)).Value.UnreadableCount.ShouldBe(1);
    }

    [Fact]
    public async Task Putting_the_content_into_the_source_column_is_detected()
    {
        await using var repository = await OpenWithThreeAsync();
        RawSet("source_app", _second.Id, RawBlob("content", _second.Id));

        (await repository.GetAsync(_second.Id, Ct)).IsFailure.ShouldBeTrue();
        (await repository.LoadSummariesAsync(Ct)).Value.UnreadableCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_format_that_does_not_exist_is_unreadable_even_if_it_gets_past_the_database_checks()
    {
        await using var repository = await OpenWithThreeAsync();
        using (var raw = Raw())
        using (var command = raw.CreateCommand())
        {
            command.CommandText = "PRAGMA ignore_check_constraints = ON; UPDATE clips SET format = 9 WHERE id = $id;";
            command.Parameters.AddWithValue("$id", _second.Id.Value.ToByteArray(bigEndian: true));
            command.ExecuteNonQuery();
        }

        (await repository.GetAsync(_second.Id, Ct)).IsFailure.ShouldBeTrue();
        (await repository.LoadAllAsync(Ct)).Value.UnreadableCount.ShouldBe(1);
        (await repository.LoadSummariesAsync(Ct)).Value.UnreadableCount.ShouldBe(1);
    }

    [Fact]
    public async Task Changing_the_format_of_a_row_is_detected()
    {
        // The format is a plain column, but the content is bound to it, so a changed format cannot be used
        // to make the content parse as something else.
        await using var repository = await OpenWithThreeAsync();
        RawSet("format", _second.Id, (long)ClipFormat.Rtf);

        (await repository.GetAsync(_second.Id, Ct)).Error.Code.ShouldBe(ErrorCode.DecryptionFailed);
    }

    [Fact]
    public async Task Changing_the_id_of_a_row_is_detected()
    {
        await using var repository = await OpenWithThreeAsync();
        var newId = ClipId.New();
        RawSet("id", _second.Id, newId.Value.ToByteArray(bigEndian: true));

        (await repository.GetAsync(newId, Ct)).Error.Code.ShouldBe(ErrorCode.DecryptionFailed);
    }

    // --- what is not protected, and what that can and cannot do ---

    [Fact]
    public async Task Changing_plain_metadata_cannot_change_what_a_clip_contains()
    {
        // Timestamps, the pin flag and sizes are plain columns. Editing them can reorder, pin or expire
        // history, which the threat model records, but it cannot reveal or alter content.
        await using var repository = await OpenWithThreeAsync();
        RawSet("is_pinned", _second.Id, 1L);
        RawSet("captured_at", _second.Id, FakeTimeProvider.Start.AddDays(1).UtcTicks);

        var loaded = (await repository.GetAsync(_second.Id, Ct)).Value;

        loaded.Content.ShouldBe(_second.Content);
        loaded.IsPinned.ShouldBeTrue();
    }

    // --- failures are recoverable ---

    [Fact]
    public async Task A_damaged_record_can_be_deleted_like_any_other()
    {
        await using var repository = await OpenWithThreeAsync();
        RawSet("content", _second.Id, new byte[] { 9 });

        (await repository.DeleteAsync([_second.Id], Ct)).Value.ShouldBe(1);

        (await repository.LoadAllAsync(Ct)).Value.UnreadableCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_error_never_carries_the_stored_text()
    {
        await using var repository = await OpenWithThreeAsync();
        var sealedValue = RawBlob("content", _second.Id);
        sealedValue[^1] ^= 1;
        RawSet("content", _second.Id, sealedValue);

        var error = (await repository.GetAsync(_second.Id, Ct)).Error;

        error.Message.ShouldNotContain("second clip");
    }
}
