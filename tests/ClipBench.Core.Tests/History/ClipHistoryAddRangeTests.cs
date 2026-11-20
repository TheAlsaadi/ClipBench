using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Tests.Properties;
using ClipBench.Core.Tests.Support;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace ClipBench.Core.Tests.History;

/// <summary>
/// Adding many clips at once, as loading the saved history does. It must leave the history exactly as adding them one
/// at a time would, while doing the work once instead of once per clip (10,000 single adds took 709 ms).
/// </summary>
public sealed class ClipHistoryAddRangeTests
{
    private readonly FakeTimeProvider _clock = new();

    private ClipHistory Create(int maxCount = 1000, long? maxBytes = null) =>
        new(new HistoryOptions { MaxCount = maxCount, MaxTotalBytes = maxBytes ?? new HistoryOptions().MaxTotalBytes }, _clock);

    private static string[] Texts(IEnumerable<Clip> clips) => [.. clips.Select(clip => ((TextContent)clip.Content).Text)];

    [Fact]
    public void Clips_added_oldest_first_end_up_newest_first()
    {
        var history = Create();

        var evicted = history.AddRange([TestClips.Text("old"), TestClips.Text("middle"), TestClips.Text("new")]);

        Texts(history.Clips).ShouldBe(["new", "middle", "old"]);
        evicted.ShouldBeEmpty();
    }

    [Fact]
    public void They_go_on_top_of_what_the_history_already_holds()
    {
        var history = Create();
        _ = history.Add(TestClips.Text("there before"));

        _ = history.AddRange([TestClips.Text("a"), TestClips.Text("b")]);

        Texts(history.Clips).ShouldBe(["b", "a", "there before"]);
    }

    [Fact]
    public void A_repeated_content_is_kept_once_with_the_first_id_the_last_time_and_a_pin_from_either()
    {
        var history = Create();
        var first = TestClips.Text("same", TestClips.Start.AddMinutes(1), source: "a.exe", pinned: true);
        var later = TestClips.Text("same", TestClips.Start.AddMinutes(3), source: "b.exe");

        _ = history.AddRange([first, TestClips.Text("other", TestClips.Start.AddMinutes(2)), later]);

        var kept = history.Clips.Where(clip => clip.Content.Equals(first.Content)).ShouldHaveSingleItem();
        kept.Id.ShouldBe(first.Id);
        kept.CapturedAt.ShouldBe(later.CapturedAt);
        kept.SourceApp.ShouldBe("b.exe");
        kept.IsPinned.ShouldBeTrue();
        Texts(history.Clips).ShouldBe(["same", "other"]);
    }

    [Fact]
    public void The_limits_apply_once_at_the_end_and_the_clips_they_push_out_are_returned()
    {
        var history = Create(maxCount: 2);

        var evicted = history.AddRange([TestClips.Text("1"), TestClips.Text("2"), TestClips.Text("3"), TestClips.Text("4")]);

        Texts(history.Clips).ShouldBe(["4", "3"]);
        Texts(evicted).ShouldBe(["2", "1"], ignoreOrder: true);
    }

    [Fact]
    public void Nothing_to_add_changes_nothing()
    {
        var history = Create();
        _ = history.Add(TestClips.Text("kept"));

        history.AddRange([]).ShouldBeEmpty();

        Texts(history.Clips).ShouldBe(["kept"]);
    }

    [Fact]
    public void A_missing_list_is_a_bug()
    {
        _ = Should.Throw<ArgumentNullException>(() => Create().AddRange(null!));
    }

    // Any mix of new and repeated texts, pins and sizes, under any count and size limit: the result is the same as one
    // add per clip. Repeats are drawn from a few texts so that they are common.
    private static readonly Gen<Clip[]> _clips =
        Gen.Zip(Gen.Zip(Gen.Elements("a", "b", "c", "d", "e", "f", "g", "h"), Gen.Choose(0, 3)), Gen.Elements(false, false, true))
            .Select(pair => TestClips.Text(pair.Item1.Item1 + new string('x', pair.Item1.Item2), pinned: pair.Item2))
            .ArrayOf()
            .Select(clips => clips.Select((clip, index) => clip with { CapturedAt = TestClips.Start.AddSeconds(index) }).ToArray());

    [Property]
    public Property Adding_many_at_once_ends_exactly_like_adding_them_one_at_a_time_when_nothing_is_pushed_out() =>
        Prop.ForAll(Arb.From(_clips), clips =>
        {
            var one = Create(maxCount: 10_000);
            var many = Create(maxCount: 10_000);
            foreach (var clip in clips)
            {
                _ = one.Add(clip);
            }

            _ = many.AddRange(clips);

            return one.Clips.SequenceEqual(many.Clips);
        });

    [Property]
    public Property With_texts_that_never_repeat_the_limits_push_out_the_same_clips_as_one_add_at_a_time() =>
        Prop.ForAll(Arb.From(Generators.Text.ArrayOf()), Arb.From(Gen.Choose(1, 30)), Arb.From(Gen.Choose(1, 2_000)), (texts, maxCount, maxBytes) =>
        {
            var distinct = texts.Distinct(StringComparer.Ordinal).Select((text, index) => TestClips.Text("#" + index + text, TestClips.Start.AddSeconds(index))).ToArray();
            var one = Create(maxCount, maxBytes);
            var many = Create(maxCount, maxBytes);
            var evictedOne = new List<Clip>();
            foreach (var clip in distinct)
            {
                evictedOne.AddRange(one.Add(clip).Evicted);
            }

            var evictedMany = many.AddRange(distinct);

            return one.Clips.SequenceEqual(many.Clips) && evictedOne.Select(clip => clip.Id.Value).Order().SequenceEqual(evictedMany.Select(clip => clip.Id.Value).Order());
        });
}
