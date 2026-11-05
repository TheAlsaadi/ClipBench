using ClipBench.Core.Boards;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.History;

public class ClipHistoryExpiryTests
{
    private static readonly TimeSpan _minute = TimeSpan.FromMinutes(1);

    private readonly FakeTimeProvider _clock = new();
    private readonly ClipHistory _history;

    public ClipHistoryExpiryTests() => _history = new ClipHistory(new HistoryOptions(), _clock);

    private Clip Expiring(string text, TimeSpan after) => TestClips.Text(text, _clock.GetUtcNow()) with { ExpiresAt = _clock.GetUtcNow() + after };

    [Fact]
    public void A_clip_has_no_expiry_unless_it_is_given_one()
    {
        TestClips.Text("plain").ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public void A_clip_keeps_its_expiry_when_it_is_added()
    {
        var clip = Expiring("secret", _minute);

        _history.Add(clip).Clip.ExpiresAt.ShouldBe(clip.ExpiresAt);
    }

    [Fact]
    public void Removing_expired_clips_takes_out_only_the_ones_whose_time_has_come()
    {
        var soon = _history.Add(Expiring("soon", _minute)).Clip;
        var later = _history.Add(Expiring("later", TimeSpan.FromMinutes(5))).Clip;
        var never = _history.Add(TestClips.Text("never", _clock.GetUtcNow())).Clip;
        _clock.Advance(TimeSpan.FromMinutes(2));

        var removed = _history.RemoveExpired();

        removed.ShouldBe([soon]);
        _history.Clips.ShouldBe([never, later]);
    }

    [Fact]
    public void A_clip_expires_at_the_moment_of_its_expiry_and_not_a_moment_before()
    {
        _history.Add(Expiring("secret", _minute));
        _clock.Advance(_minute - TimeSpan.FromTicks(1));
        _history.RemoveExpired().ShouldBeEmpty();

        _clock.Advance(TimeSpan.FromTicks(1));

        _history.RemoveExpired().Length.ShouldBe(1);
    }

    [Fact]
    public void Removing_expired_clips_when_there_are_none_changes_nothing()
    {
        var clip = _history.Add(TestClips.Text("kept", _clock.GetUtcNow())).Clip;

        _history.RemoveExpired().ShouldBeEmpty();

        _history.Clips.ShouldBe([clip]);
    }

    [Fact]
    public void Copying_a_secret_again_gives_it_the_new_expiry()
    {
        _history.Add(Expiring("secret", _minute));
        _clock.Advance(TimeSpan.FromSeconds(50));
        var again = Expiring("secret", _minute);

        var result = _history.Add(again);

        result.WasDuplicate.ShouldBeTrue();
        result.Clip.ExpiresAt.ShouldBe(again.ExpiresAt);
    }

    [Fact]
    public void Copying_a_pinned_clip_again_does_not_make_it_expire()
    {
        var pinned = _history.Add(TestClips.Text("secret", _clock.GetUtcNow())).Clip;
        _history.Pin(pinned.Id);

        var result = _history.Add(Expiring("secret", _minute));

        result.Clip.IsPinned.ShouldBeTrue();
        result.Clip.ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public void Pinning_a_clip_cancels_its_expiry_and_unpinning_does_not_bring_it_back()
    {
        var clip = _history.Add(Expiring("secret", _minute)).Clip;

        _history.Pin(clip.Id).Value.ExpiresAt.ShouldBeNull();
        _history.Unpin(clip.Id).Value.ExpiresAt.ShouldBeNull();
        _clock.Advance(TimeSpan.FromHours(1));
        _history.RemoveExpired().ShouldBeEmpty();
    }

    [Fact]
    public void Putting_a_clip_on_a_board_cancels_its_expiry()
    {
        var clip = _history.Add(Expiring("secret", _minute)).Clip;

        var placed = _history.SetBoard(clip.Id, BoardId.New());

        placed.Value.IsPinned.ShouldBeTrue();
        placed.Value.ExpiresAt.ShouldBeNull();
    }
}
