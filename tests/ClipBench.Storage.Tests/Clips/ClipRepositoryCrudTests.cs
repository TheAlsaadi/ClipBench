using System.Security.Cryptography;
using System.Text;
using ClipBench.Core.Clips;
using ClipBench.Core.Results;
using ClipBench.Storage.Clips;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests.Clips;

public sealed class ClipRepositoryCrudTests : RepositoryTestBase
{
    // --- save and read back ---

    public static TheoryData<int> FormatIndexes => new(Enumerable.Range(0, TestClips.OfEveryFormat().Length));

    [Theory]
    [MemberData(nameof(FormatIndexes))]
    public async Task Every_format_round_trips(int index)
    {
        var clip = TestClips.OfEveryFormat()[index];
        await using var repository = await OpenAsync();

        (await repository.SaveAsync(clip, Ct)).IsSuccess.ShouldBeTrue();
        var loaded = await repository.GetAsync(clip.Id, Ct);

        loaded.IsSuccess.ShouldBeTrue();
        loaded.Value.ShouldBe(clip);
    }

    [Fact]
    public async Task Every_format_survives_a_restart()
    {
        var clips = TestClips.OfEveryFormat();
        await using (var first = await OpenAsync())
        {
            await SaveAll(first, clips);
        }

        await using var second = await OpenAsync();
        var loaded = await second.LoadAllAsync(Ct);

        loaded.Value.UnreadableCount.ShouldBe(0);
        loaded.Value.Items.ShouldBe(clips.Reverse(), "newest first");
    }

    [Fact]
    public async Task The_capture_time_keeps_its_full_precision_and_its_instant()
    {
        var moment = new DateTimeOffset(2026, 10, 13, 9, 30, 15, TimeSpan.FromHours(3)).AddTicks(1234567);
        var clip = TestClips.Text("precise", moment);
        await using var repository = await OpenAsync();
        await SaveAll(repository, clip);

        var loaded = (await repository.GetAsync(clip.Id, Ct)).Value;

        loaded.CapturedAt.UtcTicks.ShouldBe(moment.UtcTicks);
        loaded.CapturedAt.ShouldBe(moment);
    }

    [Fact]
    public async Task A_source_app_round_trips_including_none_and_unicode()
    {
        var withSource = TestClips.Text("a", source: "\u0645\u062A\u0635\u0641\u062D.exe");
        var withoutSource = TestClips.Text("b", source: null);
        await using var repository = await OpenAsync();
        await SaveAll(repository, withSource, withoutSource);

        (await repository.GetAsync(withSource.Id, Ct)).Value.SourceApp.ShouldBe("\u0645\u062A\u0635\u0641\u062D.exe");
        (await repository.GetAsync(withoutSource.Id, Ct)).Value.SourceApp.ShouldBeNull();
    }

    [Fact]
    public async Task A_large_image_round_trips()
    {
        var png = RandomNumberGenerator.GetBytes(5 * 1024 * 1024);
        var clip = TestClips.Image(png);
        await using var repository = await OpenAsync();

        await SaveAll(repository, clip);

        ((ImageContent)(await repository.GetAsync(clip.Id, Ct)).Value.Content).Png.AsSpan().SequenceEqual(png).ShouldBeTrue();
    }

    [Fact]
    public async Task Getting_a_clip_that_is_not_there_fails_with_a_clear_error()
    {
        await using var repository = await OpenAsync();

        var result = await repository.GetAsync(ClipId.New(), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.ClipNotFound);
    }

    [Fact]
    public async Task Saving_the_same_id_again_updates_the_clip_in_place()
    {
        var clip = TestClips.Text("again");
        await using var repository = await OpenAsync();
        await SaveAll(repository, clip);
        var updated = clip with { CapturedAt = clip.CapturedAt.AddMinutes(5), SourceApp = "code.exe", IsPinned = true };

        await SaveAll(repository, updated);

        RawCount("SELECT COUNT(*) FROM clips").ShouldBe(1);
        (await repository.GetAsync(clip.Id, Ct)).Value.ShouldBe(updated);
    }

    [Fact]
    public async Task Two_clips_with_the_same_content_leave_one_row_and_the_newer_one_wins()
    {
        var first = TestClips.Text("same content");
        var second = TestClips.Text("same content", first.CapturedAt.AddMinutes(1));
        await using var repository = await OpenAsync();

        await SaveAll(repository, first, second);

        RawCount("SELECT COUNT(*) FROM clips").ShouldBe(1);
        (await repository.GetAsync(second.Id, Ct)).IsSuccess.ShouldBeTrue();
        (await repository.GetAsync(first.Id, Ct)).Error.Code.ShouldBe(ErrorCode.ClipNotFound);
    }

    [Fact]
    public async Task The_stored_hash_is_the_keyed_hash_of_the_format_and_the_encoded_content()
    {
        var clip = TestClips.Text("hash me");
        await using var repository = await OpenAsync();
        await SaveAll(repository, clip);

        var expected = Crypto.ComputeContentHash([(byte)ClipFormat.Text, .. Encoding.UTF8.GetBytes("hash me")]);

        RawBlob("content_hash", clip.Id).ShouldBe(expected);
        RawBlob("content_hash", clip.Id).ShouldNotBe(SHA256.HashData(Encoding.UTF8.GetBytes("hash me")));
    }

    [Fact]
    public async Task Text_and_html_with_the_same_characters_are_not_duplicates()
    {
        var text = TestClips.Text("x");
        var html = TestClips.Html("x", "x");
        await using var repository = await OpenAsync();

        await SaveAll(repository, text, html);

        RawCount("SELECT COUNT(*) FROM clips").ShouldBe(2);
    }

    [Fact]
    public async Task The_plain_columns_hold_only_what_retention_needs()
    {
        var clip = TestClips.Text("twelve bytes", source: "app.exe");
        await using var repository = await OpenAsync();
        await SaveAll(repository, clip);

        RawCount("SELECT format FROM clips").ShouldBe((long)ClipFormat.Text);
        RawCount("SELECT size_bytes FROM clips").ShouldBe(12);
        RawCount("SELECT captured_at FROM clips").ShouldBe(clip.CapturedAt.UtcTicks);
        RawCount("SELECT is_pinned FROM clips").ShouldBe(0);
        RawCount("SELECT COUNT(*) FROM clips WHERE expires_at IS NULL").ShouldBe(1);
    }

    // --- listing ---

    [Fact]
    public async Task Loading_all_returns_the_newest_clip_first()
    {
        var old = TestClips.Text("old", FakeTimeProvider.Start);
        var middle = TestClips.Text("middle", FakeTimeProvider.Start.AddMinutes(1));
        var recent = TestClips.Text("recent", FakeTimeProvider.Start.AddMinutes(2));
        await using var repository = await OpenAsync();
        await SaveAll(repository, middle, recent, old);

        var loaded = (await repository.LoadAllAsync(Ct)).Value;

        loaded.Items.Select(clip => clip.Id).ShouldBe([recent.Id, middle.Id, old.Id]);
        loaded.UnreadableCount.ShouldBe(0);
    }

    [Fact]
    public async Task Loading_an_empty_database_returns_nothing()
    {
        await using var repository = await OpenAsync();

        var loaded = (await repository.LoadAllAsync(Ct)).Value;

        loaded.Items.ShouldBeEmpty();
        loaded.UnreadableCount.ShouldBe(0);
    }

    [Fact]
    public async Task Summaries_carry_what_a_list_needs_without_the_content()
    {
        var clips = TestClips.OfEveryFormat();
        await using var repository = await OpenAsync();
        await SaveAll(repository, clips);

        var summaries = (await repository.LoadSummariesAsync(Ct)).Value;

        summaries.Items.Length.ShouldBe(clips.Length);
        foreach (var clip in clips)
        {
            var summary = summaries.Items.Single(candidate => candidate.Id == clip.Id);
            summary.Format.ShouldBe(clip.Content.Format);
            summary.CapturedAt.ShouldBe(clip.CapturedAt);
            summary.IsPinned.ShouldBe(clip.IsPinned);
            summary.SizeInBytes.ShouldBe(clip.SizeInBytes);
            summary.Preview.ShouldBe(clip.Content.GetPreview());
            summary.SourceApp.ShouldBe(clip.SourceApp);
        }

        summaries.Items.Select(summary => summary.CapturedAt).ShouldBe(clips.Select(clip => clip.CapturedAt).Reverse());
    }

    [Fact]
    public async Task A_summary_never_prints_its_preview_or_source()
    {
        var clip = TestClips.Text("secret-marker-preview", source: "secret-marker-source.exe");
        await using var repository = await OpenAsync();
        await SaveAll(repository, clip);

        var summary = (await repository.LoadSummariesAsync(Ct)).Value.Items.Single();

        summary.ToString().ShouldNotContain("secret-marker");
    }

    // --- pinning and deleting ---

    [Fact]
    public async Task Pinning_and_unpinning_persist_across_a_restart()
    {
        var clip = TestClips.Text("pin me");
        await using (var first = await OpenAsync())
        {
            await SaveAll(first, clip);
            (await first.SetPinnedAsync(clip.Id, true, Ct)).IsSuccess.ShouldBeTrue();
        }

        await using (var second = await OpenAsync())
        {
            (await second.GetAsync(clip.Id, Ct)).Value.IsPinned.ShouldBeTrue();
            (await second.SetPinnedAsync(clip.Id, false, Ct)).IsSuccess.ShouldBeTrue();
        }

        await using var third = await OpenAsync();
        (await third.GetAsync(clip.Id, Ct)).Value.IsPinned.ShouldBeFalse();
    }

    [Fact]
    public async Task Pinning_a_clip_that_is_not_there_fails_with_a_clear_error()
    {
        await using var repository = await OpenAsync();

        var result = await repository.SetPinnedAsync(ClipId.New(), true, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.ClipNotFound);
    }

    [Fact]
    public async Task Deleting_removes_exactly_the_named_clips_and_counts_them()
    {
        var keep = TestClips.Text("keep");
        var drop1 = TestClips.Text("drop one");
        var drop2 = TestClips.Text("drop two");
        await using var repository = await OpenAsync();
        await SaveAll(repository, keep, drop1, drop2);

        var removed = await repository.DeleteAsync([drop1.Id, drop2.Id, ClipId.New()], Ct);

        removed.Value.ShouldBe(2);
        (await repository.LoadAllAsync(Ct)).Value.Items.Select(clip => clip.Id).ShouldBe([keep.Id]);
    }

    [Fact]
    public async Task Deleting_nothing_succeeds()
    {
        await using var repository = await OpenAsync();

        (await repository.DeleteAsync([], Ct)).Value.ShouldBe(0);
    }

    [Fact]
    public async Task A_deleted_pinned_clip_is_gone_too()
    {
        var clip = TestClips.Text("pinned", pinned: true);
        await using var repository = await OpenAsync();
        await SaveAll(repository, clip);

        (await repository.DeleteAsync([clip.Id], Ct)).Value.ShouldBe(1);
    }

    // --- hostile input is inert ---

    [Fact]
    public async Task Text_that_looks_like_sql_is_stored_as_data_and_nothing_runs()
    {
        var clip = TestClips.Text("'; DROP TABLE clips; --", source: "Robert'); DROP TABLE clips;--");
        var path = TestClips.Files(null, "C:\\a'b\".txt", "x'); DELETE FROM clips; --");
        var html = TestClips.Html("<script>1</script>' OR '1'='1", "\" OR \"\"=\"");
        await using var repository = await OpenAsync();

        await SaveAll(repository, clip, path, html);

        RawCount("SELECT COUNT(*) FROM sqlite_master WHERE name = 'clips'").ShouldBe(1);
        RawCount("SELECT COUNT(*) FROM clips").ShouldBe(3);
        (await repository.GetAsync(clip.Id, Ct)).Value.ShouldBe(clip);
        (await repository.GetAsync(path.Id, Ct)).Value.ShouldBe(path);
        (await repository.GetAsync(html.Id, Ct)).Value.ShouldBe(html);
    }

    [Fact]
    public async Task A_clip_over_the_content_limit_is_refused_and_nothing_is_saved()
    {
        var result = await ClipRepository.OpenAsync(DatabasePath, Crypto, Clock, maxContentBytes: 50, Ct);
        await using var repository = result.Value;

        var refused = await repository.SaveAsync(TestClips.Text(new string('x', 51)), Ct);

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe(ErrorCode.ContentTooLarge);
        RawCount("SELECT COUNT(*) FROM clips").ShouldBe(0);
        (await repository.SaveAsync(TestClips.Text(new string('x', 50)), Ct)).IsSuccess.ShouldBeTrue();
    }

    // --- cancellation ---

    [Fact]
    public async Task A_cancelled_request_throws_and_changes_nothing()
    {
        await using var repository = await OpenAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => repository.SaveAsync(TestClips.Text("never"), cancelled.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => repository.LoadAllAsync(cancelled.Token));
        RawCount("SELECT COUNT(*) FROM clips").ShouldBe(0);
    }

    [Fact]
    public async Task Arguments_are_checked()
    {
        await using var repository = await OpenAsync();

        await Should.ThrowAsync<ArgumentNullException>(() => repository.SaveAsync(null!, Ct));
        await Should.ThrowAsync<ArgumentNullException>(() => repository.DeleteAsync(null!, Ct));
        await Should.ThrowAsync<ArgumentNullException>(() => repository.RetainAsync(null!, Ct));
    }
}
