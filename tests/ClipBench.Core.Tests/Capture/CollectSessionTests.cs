using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Pasting;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Capture;

public class CollectSessionTests
{
    private static readonly ClipId _newId = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    private readonly FakeTimeProvider _clock = new();

    private CollectSession Create(Separator? separator = null, long maxBytes = CollectSession.DefaultMaxBytes) =>
        new(separator ?? Separator.NewLine, _clock, () => _newId, maxBytes);

    private static CaptureRequest Request(params string[] formats) =>
        new() { Format = ClipFormat.Text, SizeInBytes = 1, FormatNames = [.. formats] };

    private static AppendOutcome Append(CollectSession session, string text) =>
        session.Append(Request("CF_UNICODETEXT"), TestClips.Text(text));

    private static string FinishedText(CollectSession session) =>
        ((TextContent)session.Finish()!.Content).Text;

    // --- the happy path ---

    [Fact]
    public void A_new_session_is_not_collecting()
    {
        var session = Create();

        session.IsActive.ShouldBeFalse();
        session.Count.ShouldBe(0);
        session.SizeInBytes.ShouldBe(0);
    }

    [Fact]
    public void Start_then_append_then_finish_gives_one_clip_with_everything_joined()
    {
        var session = Create(Separator.NewLine);

        session.Start();
        Append(session, "first quote").ShouldBe(AppendOutcome.Appended);
        Append(session, "second quote").ShouldBe(AppendOutcome.Appended);
        Append(session, "third quote").ShouldBe(AppendOutcome.Appended);

        session.IsActive.ShouldBeTrue();
        session.Count.ShouldBe(3);
        FinishedText(session).ShouldBe("first quote\r\nsecond quote\r\nthird quote");
    }

    [Fact]
    public void The_separator_is_respected()
    {
        var session = Create(Separator.Parse(" | ").Value);
        session.Start();
        Append(session, "a");
        Append(session, "b");

        FinishedText(session).ShouldBe("a | b");
    }

    [Fact]
    public void The_finished_clip_is_new_text_made_now_with_the_id_from_the_generator()
    {
        var session = Create();
        session.Start();
        Append(session, "x");
        _clock.Advance(TimeSpan.FromMinutes(7));

        var clip = session.Finish()!;

        clip.Id.ShouldBe(_newId);
        clip.CapturedAt.ShouldBe(TestClips.Start.AddMinutes(7));
        clip.Content.Format.ShouldBe(ClipFormat.Text);
        clip.SourceApp.ShouldBeNull();
        clip.IsPinned.ShouldBeFalse();
    }

    [Fact]
    public void A_single_capture_is_finished_without_a_separator()
    {
        var session = Create();
        session.Start();
        Append(session, "only");

        FinishedText(session).ShouldBe("only");
    }

    [Fact]
    public void The_order_of_the_captures_is_kept()
    {
        var session = Create(Separator.Comma);
        session.Start();
        foreach (var text in new[] { "c", "a", "b" })
        {
            Append(session, text);
        }

        FinishedText(session).ShouldBe("c,a,b");
    }

    [Fact]
    public void The_size_tracks_the_joined_text_in_utf8_bytes()
    {
        var session = Create(Separator.CommaSpace);
        session.Start();

        Append(session, "ab");
        session.SizeInBytes.ShouldBe(2);

        Append(session, "\u00E9");
        session.SizeInBytes.ShouldBe(2 + 2 + 2);
    }

    [Fact]
    public void Rich_text_and_file_captures_contribute_their_text()
    {
        var session = Create(Separator.Space);
        session.Start();
        session.Append(Request(), new Clip(ClipId.New(), new HtmlContent("<b>bold</b>", "bold"), TestClips.Start))
            .ShouldBe(AppendOutcome.Appended);
        session.Append(Request(), TestClips.Files(null, @"C:\a.txt")).ShouldBe(AppendOutcome.Appended);

        FinishedText(session).ShouldBe("bold C:\\a.txt");
    }

    // --- ending a session ---

    [Fact]
    public void Finishing_ends_the_session()
    {
        var session = Create();
        session.Start();
        Append(session, "x");

        session.Finish();

        session.IsActive.ShouldBeFalse();
        session.Count.ShouldBe(0);
        session.SizeInBytes.ShouldBe(0);
        Append(session, "late").ShouldBe(AppendOutcome.NotCollecting);
        session.Finish().ShouldBeNull();
    }

    [Fact]
    public void An_empty_session_yields_nothing()
    {
        var session = Create();
        session.Start();

        session.Finish().ShouldBeNull();

        session.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Finishing_a_session_that_was_never_started_yields_nothing()
    {
        Create().Finish().ShouldBeNull();
    }

    [Fact]
    public void Cancelling_throws_away_what_was_collected()
    {
        var session = Create();
        session.Start();
        Append(session, "x");

        session.Cancel();

        session.IsActive.ShouldBeFalse();
        session.Count.ShouldBe(0);
        session.Finish().ShouldBeNull();
    }

    [Fact]
    public void Cancelling_when_nothing_is_running_does_nothing()
    {
        Should.NotThrow(() => Create().Cancel());
    }

    [Fact]
    public void A_new_session_can_start_after_the_last_one_ended()
    {
        var session = Create();
        session.Start();
        Append(session, "old");
        session.Finish();

        session.Start();
        Append(session, "new");

        FinishedText(session).ShouldBe("new");
    }

    [Fact]
    public void Starting_a_running_session_keeps_what_was_collected()
    {
        var session = Create();
        session.Start();
        Append(session, "kept");

        session.Start();

        session.Count.ShouldBe(1);
    }

    // --- what is ignored ---

    [Fact]
    public void Nothing_is_collected_before_start()
    {
        var session = Create();

        Append(session, "x").ShouldBe(AppendOutcome.NotCollecting);

        session.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("ClipBench.Internal")]
    [InlineData("clipbench.internal")]
    public void ClipBench_own_writes_are_ignored(string marker)
    {
        var session = Create();
        session.Start();
        Append(session, "real");

        var outcome = session.Append(Request("CF_UNICODETEXT", marker), TestClips.Text("our own paste"));

        outcome.ShouldBe(AppendOutcome.OwnWrite);
        session.Count.ShouldBe(1);
        FinishedText(session).ShouldBe("real");
    }

    [Fact]
    public void Images_cannot_be_collected_into_text()
    {
        var session = Create();
        session.Start();

        session.Append(Request(), TestClips.Image(1)).ShouldBe(AppendOutcome.NotText);

        session.Count.ShouldBe(0);
    }

    // --- size limit ---

    [Fact]
    public void A_capture_that_would_pass_the_size_limit_is_refused_and_the_session_carries_on()
    {
        var session = Create(Separator.CommaSpace, maxBytes: 10);
        session.Start();

        Append(session, "aaaa").ShouldBe(AppendOutcome.Appended);
        Append(session, "bbbb").ShouldBe(AppendOutcome.Appended);
        session.SizeInBytes.ShouldBe(10);

        Append(session, "c").ShouldBe(AppendOutcome.LimitReached);
        session.Count.ShouldBe(2);
        session.IsActive.ShouldBeTrue();
        FinishedText(session).ShouldBe("aaaa, bbbb");
    }

    [Fact]
    public void The_limit_counts_utf8_bytes_not_characters()
    {
        var session = Create(Separator.None, maxBytes: 6);
        session.Start();

        // Three Arabic letters are six bytes.
        Append(session, "\u0645\u0631\u062D").ShouldBe(AppendOutcome.Appended);
        Append(session, "a").ShouldBe(AppendOutcome.LimitReached);
    }

    [Fact]
    public void The_first_capture_can_fill_the_session_exactly_but_not_exceed_it()
    {
        var session = Create(Separator.NewLine, maxBytes: 5);
        session.Start();

        Append(session, "123456").ShouldBe(AppendOutcome.LimitReached);
        Append(session, "12345").ShouldBe(AppendOutcome.Appended);
    }

    [Fact]
    public void The_default_limit_is_the_default_text_capture_limit()
    {
        CollectSession.DefaultMaxBytes.ShouldBe(CaptureLimits.Default.MaxTextBytes);
    }

    // --- bugs ---

    [Fact]
    public void Missing_arguments_and_a_limit_below_one_are_bugs()
    {
        Should.Throw<ArgumentNullException>(() => new CollectSession(null!, _clock, () => _newId));
        Should.Throw<ArgumentNullException>(() => new CollectSession(Separator.Space, null!, () => _newId));
        Should.Throw<ArgumentNullException>(() => new CollectSession(Separator.Space, _clock, null!));
        Should.Throw<ArgumentOutOfRangeException>(() => new CollectSession(Separator.Space, _clock, () => _newId, 0));

        var session = Create();
        Should.Throw<ArgumentNullException>(() => session.Append(null!, TestClips.Text("x")));
        Should.Throw<ArgumentNullException>(() => session.Append(Request(), null!));
    }

    [Fact]
    public async Task Captures_arriving_on_many_threads_are_all_kept()
    {
        var session = Create(Separator.None);
        session.Start();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                Append(session, "x");
            }
        })));

        session.Count.ShouldBe(400);
        FinishedText(session).Length.ShouldBe(400);
    }
}
