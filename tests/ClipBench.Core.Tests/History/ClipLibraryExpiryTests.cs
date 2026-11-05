using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.History;

public sealed class ClipLibraryExpiryTests : IDisposable
{
    private const string Jwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

    private static readonly TimeSpan _twoMinutes = TimeSpan.FromMinutes(2);

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeClipStore _store = new();
    private readonly ClipLibrary _library;

    public ClipLibraryExpiryTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _clock), _store) { SensitiveExpiry = _twoMinutes };
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _library.Dispose();

    private Clip Text(string text, bool pinned = false) => TestClips.Text(text, _clock.GetUtcNow(), pinned: pinned);

    // --- giving a secret an expiry ---

    [Fact]
    public async Task A_clip_that_looks_like_a_secret_expires_after_the_chosen_time_in_the_library_and_the_store()
    {
        var added = await _library.AddAsync(Text(Jwt), Ct);

        var expected = _clock.GetUtcNow() + _twoMinutes;
        added.Value.Clip.ExpiresAt.ShouldBe(expected);
        _library.Clips.Single().ExpiresAt.ShouldBe(expected);
        _store.Saved.Single().ExpiresAt.ShouldBe(expected);
    }

    [Fact]
    public async Task An_ordinary_clip_never_expires()
    {
        await _library.AddAsync(Text("just some words"), Ct);

        _library.Clips.Single().ExpiresAt.ShouldBeNull();
        _store.Saved.Single().ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_pinned_secret_does_not_expire()
    {
        await _library.AddAsync(Text(Jwt, pinned: true), Ct);

        _library.Clips.Single().ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public async Task Without_a_chosen_time_nothing_expires()
    {
        _library.SensitiveExpiry = null;

        await _library.AddAsync(Text(Jwt), Ct);

        _library.Clips.Single().ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public async Task Images_and_file_lists_never_expire()
    {
        await _library.AddAsync(TestClips.Image(1), Ct);
        await _library.AddAsync(TestClips.Files(null, "C:\a.txt"), Ct);

        _library.Clips.ShouldAllBe(clip => clip.ExpiresAt == null);
    }

    [Fact]
    public async Task A_clip_that_comes_with_its_own_expiry_keeps_it()
    {
        var own = _clock.GetUtcNow().AddSeconds(30);

        await _library.AddAsync(Text(Jwt) with { ExpiresAt = own }, Ct);

        _library.Clips.Single().ExpiresAt.ShouldBe(own);
    }

    [Fact]
    public async Task Copying_the_same_secret_again_starts_the_time_again()
    {
        await _library.AddAsync(Text(Jwt), Ct);
        _clock.Advance(TimeSpan.FromSeconds(100));

        await _library.AddAsync(Text(Jwt), Ct);

        _library.Clips.Single().ExpiresAt.ShouldBe(_clock.GetUtcNow() + _twoMinutes);
    }

    [Fact]
    public void The_chosen_time_must_be_greater_than_zero()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _library.SensitiveExpiry = TimeSpan.Zero);
        Should.Throw<ArgumentOutOfRangeException>(() => _library.SensitiveExpiry = TimeSpan.FromSeconds(-1));
    }

    // --- sweeping ---

    [Fact]
    public async Task Sweeping_removes_the_secrets_whose_time_has_come_and_leaves_the_rest()
    {
        await _library.AddAsync(Text(Jwt), Ct);
        await _library.AddAsync(Text("ordinary"), Ct);
        _clock.Advance(_twoMinutes);

        var swept = await _library.SweepExpiredAsync(Ct);

        swept.IsSuccess.ShouldBeTrue();
        swept.Value.ShouldBe(1);
        _library.Clips.Select(clip => ((TextContent)clip.Content).Text).ShouldBe(["ordinary"]);
        _store.Sweeps.ShouldBe(1);
    }

    [Fact]
    public async Task Sweeping_before_the_time_has_come_removes_nothing_and_raises_nothing()
    {
        await _library.AddAsync(Text(Jwt), Ct);
        _clock.Advance(_twoMinutes - TimeSpan.FromSeconds(1));
        var raised = 0;
        _library.Changed += (_, _) => raised++;

        var swept = await _library.SweepExpiredAsync(Ct);

        swept.Value.ShouldBe(0);
        _library.Clips.Length.ShouldBe(1);
        raised.ShouldBe(0);
    }

    [Fact]
    public async Task Sweeping_tells_the_listeners_once_when_something_was_removed()
    {
        await _library.AddAsync(Text(Jwt), Ct);
        await _library.AddAsync(Text("other " + Jwt), Ct);
        _clock.Advance(_twoMinutes);
        var raised = 0;
        _library.Changed += (_, _) => raised++;

        await _library.SweepExpiredAsync(Ct);

        raised.ShouldBe(1);
    }

    [Fact]
    public async Task Sweeping_always_asks_the_store_too_so_rows_left_from_an_earlier_run_go()
    {
        _store.Swept = [ClipId.New()];

        var swept = await _library.SweepExpiredAsync(Ct);

        swept.IsSuccess.ShouldBeTrue();
        _store.Sweeps.ShouldBe(1);
    }

    [Fact]
    public async Task A_store_that_cannot_sweep_is_reported_but_the_secrets_are_gone_from_memory()
    {
        await _library.AddAsync(Text(Jwt), Ct);
        _clock.Advance(_twoMinutes);
        var failure = new ResultError(ErrorCode.StorageFailure, "the disk said no");
        _store.Failure = failure;

        var swept = await _library.SweepExpiredAsync(Ct);

        swept.IsFailure.ShouldBeTrue();
        swept.Error.ShouldBe(failure);
        _library.Clips.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pinned_secret_is_not_swept_because_pinning_cancelled_its_expiry()
    {
        var added = (await _library.AddAsync(Text(Jwt), Ct)).Value.Clip;
        await _library.SetPinnedAsync(added.Id, true, Ct);
        _clock.Advance(TimeSpan.FromHours(1));

        await _library.SweepExpiredAsync(Ct);

        _library.Clips.Length.ShouldBe(1);
    }

    [Fact]
    public async Task A_swept_secret_is_forgotten_by_the_sensitivity_cache()
    {
        var added = (await _library.AddAsync(Text(Jwt), Ct)).Value.Clip;
        _library.GetSensitivity(added).ShouldNotBe(ClipBench.Core.Privacy.SensitiveKind.None);
        _clock.Advance(_twoMinutes);

        await _library.SweepExpiredAsync(Ct);

        _library.Clips.ShouldBeEmpty();
    }
}
