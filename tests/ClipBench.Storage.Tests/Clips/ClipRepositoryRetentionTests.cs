using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Storage.Clips;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests.Clips;

public sealed class ClipRepositoryRetentionTests : RepositoryTestBase
{
    private DateTimeOffset Now => Clock.GetUtcNow();

    // A text clip of exactly this many bytes (ASCII) whose content is unique to the index.
    private static Clip Sized(int index, int bytes, DateTimeOffset capturedAt, bool pinned = false) =>
        TestClips.Text($"{index:D3}" + new string('x', bytes - 3), capturedAt, pinned: pinned);

    private static async Task<HashSet<ClipId>> Retain(ClipRepository repository, HistoryOptions options) =>
        [.. (await repository.RetainAsync(options, Ct)).Value];

    private static async Task<string[]> Remaining(ClipRepository repository) =>
        [.. (await repository.LoadAllAsync(Ct)).Value.Items.Select(clip => ((TextContent)clip.Content).Text[..3])];

    // --- by count ---

    [Fact]
    public async Task The_oldest_unpinned_clips_beyond_the_count_are_removed()
    {
        var clips = Enumerable.Range(1, 5).Select(i => Sized(i, 10, Now.AddMinutes(-(10 - i)))).ToArray();
        await using var repository = await OpenAsync();
        await SaveAll(repository, clips);

        var removed = await Retain(repository, new HistoryOptions { MaxCount = 3 });

        removed.ShouldBe([clips[0].Id, clips[1].Id], ignoreOrder: true);
        (await Remaining(repository)).ShouldBe(["005", "004", "003"]);
    }

    [Fact]
    public async Task Pinned_clips_are_never_removed_by_count_and_do_not_count()
    {
        var pinned = Sized(1, 10, Now.AddMinutes(-100), pinned: true);
        var others = Enumerable.Range(2, 4).Select(i => Sized(i, 10, Now.AddMinutes(-(10 - i)))).ToArray();
        await using var repository = await OpenAsync();
        await SaveAll(repository, [pinned, .. others]);

        var removed = await Retain(repository, new HistoryOptions { MaxCount = 2 });

        removed.ShouldBe([others[0].Id, others[1].Id], ignoreOrder: true);
        (await Remaining(repository)).ShouldBe(["005", "004", "001"]);
    }

    [Fact]
    public async Task Nothing_is_removed_when_everything_is_within_the_limits()
    {
        await using var repository = await OpenAsync();
        await SaveAll(repository, Sized(1, 10, Now), Sized(2, 10, Now.AddSeconds(-1)));

        (await Retain(repository, new HistoryOptions())).ShouldBeEmpty();
    }

    [Fact]
    public async Task Retaining_an_empty_database_removes_nothing()
    {
        await using var repository = await OpenAsync();

        (await Retain(repository, new HistoryOptions())).ShouldBeEmpty();
    }

    [Fact]
    public async Task Retaining_twice_removes_nothing_the_second_time()
    {
        await using var repository = await OpenAsync();
        await SaveAll(repository, Enumerable.Range(1, 6).Select(i => Sized(i, 10, Now.AddMinutes(-i))).ToArray());
        await Retain(repository, new HistoryOptions { MaxCount = 2 });

        (await Retain(repository, new HistoryOptions { MaxCount = 2 })).ShouldBeEmpty();
    }

    // --- by age ---

    [Fact]
    public async Task A_clip_exactly_at_the_maximum_age_is_kept_and_one_tick_older_is_removed()
    {
        var age = TimeSpan.FromHours(1);
        var atLimit = Sized(1, 10, Now - age);
        var older = Sized(2, 10, Now - age - TimeSpan.FromTicks(1));
        await using var repository = await OpenAsync();
        await SaveAll(repository, atLimit, older);

        var removed = await Retain(repository, new HistoryOptions { MaxAge = age });

        removed.ShouldBe([older.Id]);
    }

    [Fact]
    public async Task Pinned_clips_are_never_removed_by_age()
    {
        var pinned = Sized(1, 10, Now.AddDays(-400), pinned: true);
        await using var repository = await OpenAsync();
        await SaveAll(repository, pinned);

        (await Retain(repository, new HistoryOptions { MaxAge = TimeSpan.FromDays(1) })).ShouldBeEmpty();
    }

    // --- by total size ---

    [Fact]
    public async Task The_oldest_clips_are_removed_when_the_total_size_exceeds_the_budget()
    {
        var clips = new[] { Sized(1, 4, Now.AddMinutes(-3)), Sized(2, 4, Now.AddMinutes(-2)), Sized(3, 4, Now.AddMinutes(-1)) };
        await using var repository = await OpenAsync();
        await SaveAll(repository, clips);

        var removed = await Retain(repository, new HistoryOptions { MaxTotalBytes = 10 });

        removed.ShouldBe([clips[0].Id]);
    }

    [Fact]
    public async Task A_total_exactly_at_the_budget_is_kept()
    {
        await using var repository = await OpenAsync();
        await SaveAll(repository, Sized(1, 4, Now.AddMinutes(-2)), Sized(2, 4, Now.AddMinutes(-1)));

        (await Retain(repository, new HistoryOptions { MaxTotalBytes = 8 })).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_newest_clip_is_kept_even_when_it_alone_exceeds_the_budget()
    {
        await using var repository = await OpenAsync();
        await SaveAll(repository, Sized(1, 30, Now));

        (await Retain(repository, new HistoryOptions { MaxTotalBytes = 3 })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Once_the_budget_is_exceeded_older_clips_go_even_if_they_are_small()
    {
        // Newest first the sizes are 5 and 9, then 1. The 9 does not fit; the small 1 behind it must not outlive it.
        var small = Sized(1, 4, Now.AddMinutes(-3));
        var big = Sized(2, 9, Now.AddMinutes(-2));
        var newest = Sized(3, 5, Now.AddMinutes(-1));
        await using var repository = await OpenAsync();
        await SaveAll(repository, small, big, newest);

        var removed = await Retain(repository, new HistoryOptions { MaxTotalBytes = 10 });

        removed.ShouldBe([small.Id, big.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Pinned_clips_do_not_count_toward_the_size_budget()
    {
        var oldA = Sized(1, 4, Now.AddMinutes(-3));
        var oldB = Sized(2, 4, Now.AddMinutes(-2));
        var pinned = Sized(3, 100, Now.AddMinutes(-1), pinned: true);
        await using var repository = await OpenAsync();
        await SaveAll(repository, oldA, oldB, pinned);

        (await Retain(repository, new HistoryOptions { MaxTotalBytes = 8 })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Images_count_by_their_byte_size()
    {
        var older = TestClips.Image([1, 2, 3], Now.AddMinutes(-2));
        var newer = TestClips.Image([4, 5, 6], Now.AddMinutes(-1));
        await using var repository = await OpenAsync();
        await SaveAll(repository, older, newer);

        (await Retain(repository, new HistoryOptions { MaxTotalBytes = 5 })).ShouldBe([older.Id]);
    }

    // --- the stored rules and the in-memory rules are the same rules ---

    [Fact]
    public async Task Retention_removes_exactly_what_the_in_memory_history_would()
    {
        var random = new Random(20261013);
        await using var repository = await OpenAsync();

        for (var round = 0; round < 250; round++)
        {
            var options = new HistoryOptions
            {
                MaxCount = random.Next(1, 8),
                MaxAge = TimeSpan.FromMinutes(random.Next(10, 120)),
                MaxTotalBytes = random.Next(4, 200),
            };

            // Distinct capture times, so the order is the same in the list and in the database.
            var clips = Enumerable.Range(0, random.Next(0, 14))
                .Select(i => Sized(
                    i,
                    random.Next(4, 60),
                    Now.AddMinutes(-random.Next(0, 180)).AddSeconds(-i),
                    pinned: random.Next(4) == 0))
                .OrderBy(clip => clip.CapturedAt)
                .ToList();

            // Pinned clips are exempt while they are added, so the history holds all of them for one trim.
            var history = new ClipHistory(options, Clock);
            foreach (var clip in clips)
            {
                history.Pin(history.Add(clip with { IsPinned = true }).Clip.Id);
            }

            foreach (var clip in clips.Where(clip => !clip.IsPinned))
            {
                history.Unpin(clip.Id);
            }

            var expected = history.Trim().Select(clip => clip.Id).ToHashSet();

            await SaveAll(repository, [.. clips]);
            var removed = await Retain(repository, options);

            removed.ShouldBe(expected, ignoreOrder: true, $"round {round}: {options.MaxCount} clips, {options.MaxAge}, {options.MaxTotalBytes} bytes");
            await repository.DeleteAsync(clips.Select(clip => clip.Id), Ct);
        }
    }

    // --- expiry ---

    [Fact]
    public async Task Retaining_also_removes_expired_clips_so_they_never_take_a_slot_from_a_live_one()
    {
        var expired = Sized(1, 10, Now.AddMinutes(-1));
        var live = Sized(2, 10, Now.AddMinutes(-2));
        await using var repository = await OpenAsync();
        await repository.SaveAsync(expired, Now.AddSeconds(-1), Ct);
        await repository.SaveAsync(live, Ct);

        var removed = await Retain(repository, new HistoryOptions { MaxCount = 1 });

        removed.ShouldBe([expired.Id]);
        (await Remaining(repository)).ShouldBe(["002"]);
    }

    [Fact]
    public async Task Sweeping_removes_expired_clips_and_returns_their_ids()
    {
        var expired = TestClips.Text("expired");
        var later = TestClips.Text("later");
        var never = TestClips.Text("never");
        await using var repository = await OpenAsync();
        await repository.SaveAsync(expired, Now.AddSeconds(-1), Ct);
        await repository.SaveAsync(later, Now.AddMinutes(2), Ct);
        await repository.SaveAsync(never, Ct);

        var swept = (await repository.SweepExpiredAsync(Ct)).Value;

        swept.ShouldBe([expired.Id]);
        (await repository.LoadAllAsync(Ct)).Value.Items.Select(clip => clip.Id).ShouldBe([later.Id, never.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task A_clip_expires_at_the_moment_its_expiry_arrives()
    {
        var clip = TestClips.Text("at the second");
        await using var repository = await OpenAsync();
        await repository.SaveAsync(clip, Now.AddMinutes(1), Ct);
        (await repository.SweepExpiredAsync(Ct)).Value.ShouldBeEmpty();

        Clock.Advance(TimeSpan.FromMinutes(1));

        (await repository.SweepExpiredAsync(Ct)).Value.ShouldBe([clip.Id]);
    }

    [Fact]
    public async Task An_expired_clip_that_was_not_swept_yet_is_invisible()
    {
        var clip = TestClips.Text("stale");
        await using var repository = await OpenAsync();
        await repository.SaveAsync(clip, Now.AddMinutes(1), Ct);
        Clock.Advance(TimeSpan.FromMinutes(2));

        (await repository.LoadAllAsync(Ct)).Value.Items.ShouldBeEmpty();
        (await repository.LoadSummariesAsync(Ct)).Value.Items.ShouldBeEmpty();
        (await repository.GetAsync(clip.Id, Ct)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Saving_again_without_an_expiry_clears_it()
    {
        var clip = TestClips.Text("no longer sensitive");
        await using var repository = await OpenAsync();
        await repository.SaveAsync(clip, Now.AddMinutes(1), Ct);
        await repository.SaveAsync(clip, Ct);
        Clock.Advance(TimeSpan.FromHours(1));

        (await repository.SweepExpiredAsync(Ct)).Value.ShouldBeEmpty();
        (await repository.GetAsync(clip.Id, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task An_expiry_removes_even_a_pinned_clip_because_it_was_asked_for_explicitly()
    {
        var clip = TestClips.Text("pinned but sensitive", pinned: true);
        await using var repository = await OpenAsync();
        await repository.SaveAsync(clip, Now.AddSeconds(-1), Ct);

        (await repository.SweepExpiredAsync(Ct)).Value.ShouldBe([clip.Id]);
    }

    [Fact]
    public async Task Sweeping_with_nothing_expired_removes_nothing()
    {
        await using var repository = await OpenAsync();
        await SaveAll(repository, TestClips.Text("fine"));

        (await repository.SweepExpiredAsync(Ct)).Value.ShouldBeEmpty();
    }
}
