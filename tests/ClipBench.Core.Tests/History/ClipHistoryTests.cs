using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.History;

public class ClipHistoryTests
{
    private readonly FakeTimeProvider _clock = new();

    private ClipHistory Create(int maxCount = 1000, TimeSpan? maxAge = null) =>
        new(new HistoryOptions { MaxCount = maxCount, MaxAge = maxAge ?? TimeSpan.FromDays(30) }, _clock);

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
    public void The_default_limits_are_a_thousand_clips_and_thirty_days()
    {
        var options = new HistoryOptions();

        options.MaxCount.ShouldBe(1000);
        options.MaxAge.ShouldBe(TimeSpan.FromDays(30));
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
