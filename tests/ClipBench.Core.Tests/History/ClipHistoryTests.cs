using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.History;

public class ClipHistoryTests
{
    private readonly FakeTimeProvider _clock = new();

    private ClipHistory Create(int maxCount = 1000, TimeSpan? maxAge = null, long? maxBytes = null) =>
        new(
            new HistoryOptions
            {
                MaxCount = maxCount,
                MaxAge = maxAge ?? TimeSpan.FromDays(30),
                MaxTotalBytes = maxBytes ?? new HistoryOptions().MaxTotalBytes,
            },
            _clock);

    // A text clip of exactly this many bytes (ASCII, so one byte per character).
    private static Clip Sized(int bytes, char fill = 'x') => TestClips.Text(new string(fill, bytes));

    private static string[] Texts(ClipHistory history) =>
        [.. history.Clips.Select(clip => ((TextContent)clip.Content).Text)];

    // --- adding and ordering ---

    [Fact]
    public void A_new_history_is_empty()
    {
        var history = Create();

        history.Clips.ShouldBeEmpty();
        history.Count.ShouldBe(0);
    }

    [Fact]
    public void The_newest_clip_comes_first()
    {
        var history = Create();

        history.Add(TestClips.Text("one"));
        history.Add(TestClips.Text("two"));
        history.Add(TestClips.Text("three"));

        Texts(history).ShouldBe(["three", "two", "one"]);
    }

    [Fact]
    public void Adding_a_new_clip_reports_that_it_was_not_a_duplicate()
    {
        var clip = TestClips.Text("one");

        var result = Create().Add(clip);

        result.WasDuplicate.ShouldBeFalse();
        result.Clip.ShouldBe(clip);
        result.Evicted.ShouldBeEmpty();
    }

    [Fact]
    public void Adding_a_duplicate_moves_it_to_the_top_instead_of_duplicating_it()
    {
        var history = Create();
        history.Add(TestClips.Text("one"));
        history.Add(TestClips.Text("two"));
        history.Add(TestClips.Text("three"));

        var result = history.Add(TestClips.Text("one"));

        result.WasDuplicate.ShouldBeTrue();
        Texts(history).ShouldBe(["one", "three", "two"]);
        history.Count.ShouldBe(3);
    }

    [Fact]
    public void A_duplicate_keeps_the_original_id_and_takes_the_new_time_and_source()
    {
        var history = Create();
        var original = TestClips.Text("same", TestClips.Start, "notepad.exe");
        history.Add(original);
        var later = TestClips.Start.AddMinutes(5);

        var result = history.Add(TestClips.Text("same", later, "code.exe"));

        result.Clip.Id.ShouldBe(original.Id);
        result.Clip.CapturedAt.ShouldBe(later);
        result.Clip.SourceApp.ShouldBe("code.exe");
        history.Clips.Single().ShouldBe(result.Clip);
    }

    [Fact]
    public void A_pinned_clip_stays_pinned_when_it_is_copied_again()
    {
        var history = Create();
        var original = history.Add(TestClips.Text("same")).Clip;
        history.Pin(original.Id);

        var result = history.Add(TestClips.Text("same"));

        result.Clip.IsPinned.ShouldBeTrue();
        history.Clips.Single().IsPinned.ShouldBeTrue();
    }

    [Fact]
    public void Content_of_a_different_format_is_not_a_duplicate_even_with_the_same_text()
    {
        var history = Create();
        history.Add(TestClips.Text("same"));

        var result = history.Add(new Clip(ClipId.New(), new HtmlContent("same", "same"), TestClips.Start));

        result.WasDuplicate.ShouldBeFalse();
        history.Count.ShouldBe(2);
    }

    [Fact]
    public void Images_with_the_same_bytes_are_duplicates()
    {
        var history = Create();
        history.Add(TestClips.Image(1));
        history.Add(TestClips.Image(2));

        var result = history.Add(TestClips.Image(1));

        result.WasDuplicate.ShouldBeTrue();
        history.Count.ShouldBe(2);
    }

    [Fact]
    public void Adding_nothing_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => Create().Add(null!));
    }

    [Fact]
    public void A_snapshot_taken_earlier_does_not_change_when_the_history_does()
    {
        var history = Create();
        history.Add(TestClips.Text("one"));
        var snapshot = history.Clips;

        history.Add(TestClips.Text("two"));

        snapshot.Length.ShouldBe(1);
        history.Count.ShouldBe(2);
    }

    // --- lookup ---

    [Fact]
    public void A_clip_can_be_found_by_id()
    {
        var history = Create();
        var clip = history.Add(TestClips.Text("one")).Clip;

        history.Get(clip.Id).ShouldBe(clip);
        history.Get(ClipId.New()).ShouldBeNull();
    }

    // --- eviction by count ---

    [Fact]
    public void The_oldest_clips_are_evicted_when_the_count_limit_is_exceeded()
    {
        var history = Create(maxCount: 3);
        var first = history.Add(TestClips.Text("one")).Clip;
        var second = history.Add(TestClips.Text("two")).Clip;
        history.Add(TestClips.Text("three"));
        history.Add(TestClips.Text("four"));

        var result = history.Add(TestClips.Text("five"));

        Texts(history).ShouldBe(["five", "four", "three"]);
        result.Evicted.ShouldBe([second]);
        history.Get(first.Id).ShouldBeNull();
    }

    [Fact]
    public void Eviction_reports_every_clip_it_removed()
    {
        var history = Create(maxCount: 1);
        var one = history.Add(TestClips.Text("one")).Clip;

        var two = history.Add(TestClips.Text("two"));

        two.Evicted.ShouldBe([one]);
    }

    [Fact]
    public void Pinned_clips_are_never_evicted_by_count_and_do_not_count_toward_the_limit()
    {
        var history = Create(maxCount: 2);
        var pinned = history.Add(TestClips.Text("keep me")).Clip;
        history.Pin(pinned.Id);

        history.Add(TestClips.Text("one"));
        history.Add(TestClips.Text("two"));
        history.Add(TestClips.Text("three"));

        Texts(history).ShouldBe(["three", "two", "keep me"]);
        history.Get(pinned.Id)!.IsPinned.ShouldBeTrue();
    }

    [Fact]
    public void Copying_the_same_thing_many_times_does_not_fill_the_history()
    {
        var history = Create(maxCount: 2);

        for (var i = 0; i < 10; i++)
        {
            history.Add(TestClips.Text("again"));
        }

        history.Count.ShouldBe(1);
    }

    // --- eviction by total size ---

    [Fact]
    public void The_oldest_clips_are_evicted_when_the_total_size_exceeds_the_budget()
    {
        var history = Create(maxBytes: 10);
        var oldest = history.Add(Sized(4, 'a')).Clip;
        history.Add(Sized(4, 'b'));

        var result = history.Add(Sized(4, 'c'));

        Texts(history).ShouldBe(["cccc", "bbbb"]);
        result.Evicted.ShouldBe([oldest]);
    }

    [Fact]
    public void A_total_exactly_at_the_budget_is_kept()
    {
        var history = Create(maxBytes: 8);
        history.Add(Sized(4, 'a'));

        var result = history.Add(Sized(4, 'b'));

        history.Count.ShouldBe(2);
        result.Evicted.ShouldBeEmpty();
    }

    [Fact]
    public void Pinned_clips_are_never_evicted_by_size_and_do_not_count_toward_the_budget()
    {
        // The pinned clip is the newest, so a history that counted it would push out the two older clips.
        var history = Create(maxBytes: 6);
        history.Add(Sized(3, 'a'));
        history.Add(Sized(3, 'b'));

        var pinned = history.Add(TestClips.Text(new string('p', 100), pinned: true)).Clip;

        history.Count.ShouldBe(3);
        history.Trim().ShouldBeEmpty();
        history.Get(pinned.Id)!.IsPinned.ShouldBeTrue();
    }

    [Fact]
    public void The_newest_clip_is_kept_even_when_it_alone_exceeds_the_budget()
    {
        var history = Create(maxBytes: 3);

        var result = history.Add(Sized(10, 'a'));

        Texts(history).ShouldBe([new string('a', 10)]);
        result.Evicted.ShouldBeEmpty();
    }

    [Fact]
    public void A_clip_that_exceeds_the_budget_pushes_out_everything_older()
    {
        var history = Create(maxBytes: 10);

        history.Add(Sized(10, 'a'));
        var result = history.Add(Sized(10, 'b'));

        Texts(history).ShouldBe([new string('b', 10)]);
        result.Evicted.Length.ShouldBe(1);
    }

    [Fact]
    public void Once_the_budget_is_exceeded_older_clips_are_evicted_even_if_they_are_small()
    {
        // Newest first the sizes are 2, 5, 9, 1. The 9 does not fit, and the small 1 behind it must
        // not survive it: keeping an older clip over a newer one would make the history unpredictable.
        // The clips are pinned while they are added so that the single trim below sees all four.
        var history = Create(maxBytes: 10);
        var clips = new[] { Sized(1, 'd'), Sized(9, 'c'), Sized(5, 'b'), Sized(2, 'a') };
        foreach (var clip in clips)
        {
            history.Pin(history.Add(clip).Clip.Id);
        }

        foreach (var clip in clips)
        {
            history.Unpin(clip.Id);
        }

        var evicted = history.Trim();

        Texts(history).ShouldBe(["aa", "bbbbb"]);
        evicted.Select(clip => clip.Id).ShouldBe([clips[1].Id, clips[0].Id]);
    }

    [Fact]
    public void Images_count_by_their_byte_size()
    {
        var history = Create(maxBytes: 5);
        history.Add(TestClips.Image(1)); // 3 bytes

        var result = history.Add(TestClips.Image(2));

        history.Count.ShouldBe(1);
        result.Evicted.Length.ShouldBe(1);
    }

    [Fact]
    public void A_trim_applies_the_budget_after_a_large_clip_is_unpinned()
    {
        var history = Create(maxBytes: 10);
        var large = history.Add(Sized(9, 'l')).Clip;
        history.Pin(large.Id);
        history.Add(Sized(5, 'a'));
        history.Unpin(large.Id);
        history.Count.ShouldBe(2);

        var evicted = history.Trim();

        evicted.ShouldBe([large]);
        Texts(history).ShouldBe(["aaaaa"]);
    }

    [Fact]
    public void A_clip_that_is_too_old_does_not_count_toward_the_budget()
    {
        // The list is C, B, A. B was captured before A (A was stamped ahead of the clock), so B expires
        // first. Its 9 bytes must not spend the budget and push out A, which still fits next to C.
        var history = Create(maxAge: TimeSpan.FromHours(1), maxBytes: 12);
        var start = _clock.GetUtcNow();
        var a = history.Add(TestClips.Text("aa", start + TimeSpan.FromMinutes(30))).Clip;
        history.Add(TestClips.Text(new string('b', 9), start));
        history.Count.ShouldBe(2);
        _clock.Advance(TimeSpan.FromMinutes(70));

        history.Add(TestClips.Text("ccccc", _clock.GetUtcNow()));

        Texts(history).ShouldBe(["ccccc", "aa"]);
        history.Get(a.Id).ShouldNotBeNull();
    }

    // --- eviction by age ---

    [Fact]
    public void Clips_older_than_the_maximum_age_are_removed_by_a_trim()
    {
        var history = Create(maxAge: TimeSpan.FromHours(1));
        var old = history.Add(TestClips.Text("old", _clock.GetUtcNow())).Clip;
        _clock.Advance(TimeSpan.FromMinutes(30));
        history.Add(TestClips.Text("recent", _clock.GetUtcNow()));
        _clock.Advance(TimeSpan.FromMinutes(31));

        var removed = history.Trim();

        removed.ShouldBe([old]);
        Texts(history).ShouldBe(["recent"]);
    }

    [Fact]
    public void A_clip_exactly_at_the_maximum_age_is_kept_and_one_tick_older_is_removed()
    {
        var history = Create(maxAge: TimeSpan.FromHours(1));
        history.Add(TestClips.Text("edge", _clock.GetUtcNow()));

        _clock.Advance(TimeSpan.FromHours(1));
        history.Trim().ShouldBeEmpty();
        history.Count.ShouldBe(1);

        _clock.Advance(TimeSpan.FromTicks(1));
        history.Trim().Length.ShouldBe(1);
        history.Count.ShouldBe(0);
    }

    [Fact]
    public void Adding_a_clip_also_removes_clips_that_have_become_too_old()
    {
        var history = Create(maxAge: TimeSpan.FromHours(1));
        var old = history.Add(TestClips.Text("old", _clock.GetUtcNow())).Clip;
        _clock.Advance(TimeSpan.FromHours(2));

        var result = history.Add(TestClips.Text("new", _clock.GetUtcNow()));

        result.Evicted.ShouldBe([old]);
        Texts(history).ShouldBe(["new"]);
    }

    [Fact]
    public void Pinned_clips_are_never_evicted_by_age()
    {
        var history = Create(maxAge: TimeSpan.FromHours(1));
        var pinned = history.Add(TestClips.Text("pinned", _clock.GetUtcNow())).Clip;
        history.Pin(pinned.Id);
        _clock.Advance(TimeSpan.FromDays(365));

        history.Trim().ShouldBeEmpty();

        history.Get(pinned.Id).ShouldNotBeNull();
    }

    [Fact]
    public void Copying_a_clip_again_restarts_its_age()
    {
        var history = Create(maxAge: TimeSpan.FromHours(1));
        history.Add(TestClips.Text("same", _clock.GetUtcNow()));
        _clock.Advance(TimeSpan.FromMinutes(50));
        history.Add(TestClips.Text("same", _clock.GetUtcNow()));
        _clock.Advance(TimeSpan.FromMinutes(50));

        history.Trim().ShouldBeEmpty();

        history.Count.ShouldBe(1);
    }

    [Fact]
    public void A_clip_that_is_already_too_old_when_added_is_evicted_at_once()
    {
        var history = Create(maxAge: TimeSpan.FromHours(1));
        var stale = TestClips.Text("stale", _clock.GetUtcNow().AddHours(-2));

        var result = history.Add(stale);

        result.Evicted.ShouldBe([stale]);
        history.Count.ShouldBe(0);
    }

    [Fact]
    public void A_clip_stamped_in_the_future_is_not_treated_as_old()
    {
        var history = Create(maxAge: TimeSpan.FromHours(1));
        history.Add(TestClips.Text("future", _clock.GetUtcNow().AddDays(1)));

        history.Trim().ShouldBeEmpty();
    }

    // --- pin, unpin, remove, clear ---

    [Fact]
    public void Pinning_marks_the_clip_and_keeps_its_place()
    {
        var history = Create();
        var first = history.Add(TestClips.Text("one")).Clip;
        history.Add(TestClips.Text("two"));

        var result = history.Pin(first.Id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsPinned.ShouldBeTrue();
        Texts(history).ShouldBe(["two", "one"]);
        history.Get(first.Id)!.IsPinned.ShouldBeTrue();
    }

    [Fact]
    public void Pinning_a_pinned_clip_succeeds_and_changes_nothing()
    {
        var history = Create();
        var clip = history.Add(TestClips.Text("one")).Clip;
        history.Pin(clip.Id);

        history.Pin(clip.Id).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Unpinning_clears_the_mark()
    {
        var history = Create();
        var clip = history.Add(TestClips.Text("one")).Clip;
        history.Pin(clip.Id);

        var result = history.Unpin(clip.Id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsPinned.ShouldBeFalse();
        history.Get(clip.Id)!.IsPinned.ShouldBeFalse();
    }

    [Fact]
    public void Pinning_or_unpinning_an_unknown_clip_fails_with_a_clear_error()
    {
        var history = Create();

        history.Pin(ClipId.New()).Error.Code.ShouldBe(ErrorCode.ClipNotFound);
        history.Unpin(ClipId.New()).Error.Code.ShouldBe(ErrorCode.ClipNotFound);
    }

    [Fact]
    public void Unpinning_does_not_evict_until_the_next_trim()
    {
        var history = Create(maxCount: 1);
        var pinned = history.Add(TestClips.Text("pinned")).Clip;
        history.Pin(pinned.Id);
        history.Add(TestClips.Text("recent"));

        var unpinned = history.Unpin(pinned.Id).Value;
        history.Count.ShouldBe(2);

        history.Trim().ShouldBe([unpinned]);
        Texts(history).ShouldBe(["recent"]);
    }

    [Fact]
    public void Removing_a_clip_returns_it_and_forgets_it()
    {
        var history = Create();
        var clip = history.Add(TestClips.Text("one")).Clip;

        history.Remove(clip.Id).ShouldBe(clip);

        history.Count.ShouldBe(0);
        history.Remove(clip.Id).ShouldBeNull();
    }

    [Fact]
    public void A_pinned_clip_can_be_removed_by_the_user()
    {
        var history = Create();
        var clip = history.Add(TestClips.Text("one")).Clip;
        history.Pin(clip.Id);

        history.Remove(clip.Id).ShouldNotBeNull();

        history.Count.ShouldBe(0);
    }

    [Fact]
    public void Clear_removes_unpinned_clips_and_keeps_pinned_ones()
    {
        var history = Create();
        var pinned = history.Add(TestClips.Text("keep")).Clip;
        history.Pin(pinned.Id);
        var other = history.Add(TestClips.Text("drop")).Clip;

        var removed = history.Clear();

        removed.ShouldBe([other]);
        Texts(history).ShouldBe(["keep"]);
    }

    [Fact]
    public void Clear_can_remove_pinned_clips_too()
    {
        var history = Create();
        var pinned = history.Add(TestClips.Text("keep")).Clip;
        history.Pin(pinned.Id);
        history.Add(TestClips.Text("drop"));

        var removed = history.Clear(includePinned: true);

        removed.Length.ShouldBe(2);
        history.Count.ShouldBe(0);
    }

    // --- options, construction and concurrency ---

    [Fact]
    public void The_default_limits_are_a_thousand_clips_thirty_days_and_256_mebibytes()
    {
        var options = new HistoryOptions();

        options.MaxCount.ShouldBe(1000);
        options.MaxAge.ShouldBe(TimeSpan.FromDays(30));
        options.MaxTotalBytes.ShouldBe(256L * 1024 * 1024);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_size_budget_below_one_byte_is_a_bug(long bytes)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new HistoryOptions { MaxTotalBytes = bytes });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_count_limit_below_one_is_a_bug(int count)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new HistoryOptions { MaxCount = count });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_age_limit_that_is_not_positive_is_a_bug(long ticks)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new HistoryOptions { MaxAge = TimeSpan.FromTicks(ticks) });
    }

    [Fact]
    public void A_history_needs_options_and_a_clock()
    {
        Should.Throw<ArgumentNullException>(() => new ClipHistory(null!, _clock));
        Should.Throw<ArgumentNullException>(() => new ClipHistory(new HistoryOptions(), null!));
    }

    [Fact]
    public async Task Concurrent_adds_from_many_threads_lose_nothing()
    {
        var history = Create(maxCount: 10_000);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(thread => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                history.Add(TestClips.Text($"clip {thread}-{i}"));
            }
        })));

        history.Count.ShouldBe(1600);
        history.Clips.Select(clip => clip.Id).Distinct().Count().ShouldBe(1600);
    }
}
