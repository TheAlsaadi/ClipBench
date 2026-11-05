using ClipBench.Core.Clips;
using ClipBench.Storage.Clips;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests.Clips;

/// <summary>A clip that looks like a secret carries its expiry with it, and the expiry survives a restart.</summary>
public sealed class ClipRepositoryExpiryTests : RepositoryTestBase
{
    private static readonly TimeSpan _twoMinutes = TimeSpan.FromMinutes(2);

    private DateTimeOffset Now => Clock.GetUtcNow();

    private Clip Secret(string text) => TestClips.Text(text, Now) with { ExpiresAt = Now + _twoMinutes };

    [Fact]
    public async Task A_clip_saved_with_an_expiry_comes_back_with_it()
    {
        var clip = Secret("token");
        await using var repository = await OpenAsync();
        await repository.SaveAsync(clip, Ct);

        (await repository.GetAsync(clip.Id, Ct)).Value.ExpiresAt.ShouldBe(clip.ExpiresAt);
        (await repository.LoadAllAsync(Ct)).Value.Items.Single().ExpiresAt.ShouldBe(clip.ExpiresAt);
    }

    [Fact]
    public async Task A_clip_saved_without_an_expiry_comes_back_without_one()
    {
        var clip = TestClips.Text("ordinary", Now);
        await using var repository = await OpenAsync();
        await repository.SaveAsync(clip, Ct);

        (await repository.GetAsync(clip.Id, Ct)).Value.ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_clip_saved_with_an_expiry_is_swept_when_the_time_comes()
    {
        var clip = Secret("token");
        await using var repository = await OpenAsync();
        await repository.SaveAsync(clip, Ct);
        (await repository.SweepExpiredAsync(Ct)).Value.ShouldBeEmpty();

        Clock.Advance(_twoMinutes);

        (await repository.SweepExpiredAsync(Ct)).Value.ShouldBe([clip.Id]);
        (await repository.LoadAllAsync(Ct)).Value.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Pinning_a_clip_cancels_its_expiry_in_the_database()
    {
        var clip = Secret("token");
        await using var repository = await OpenAsync();
        await repository.SaveAsync(clip, Ct);

        (await repository.SetPinnedAsync(clip.Id, true, Ct)).IsSuccess.ShouldBeTrue();
        Clock.Advance(TimeSpan.FromHours(1));

        (await repository.SweepExpiredAsync(Ct)).Value.ShouldBeEmpty();
        var kept = (await repository.GetAsync(clip.Id, Ct)).Value;
        kept.IsPinned.ShouldBeTrue();
        kept.ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public async Task Unpinning_a_clip_does_not_give_it_an_expiry()
    {
        var clip = TestClips.Text("kept", Now, pinned: true);
        await using var repository = await OpenAsync();
        await repository.SaveAsync(clip, Ct);

        await repository.SetPinnedAsync(clip.Id, false, Ct);

        (await repository.GetAsync(clip.Id, Ct)).Value.ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public async Task The_store_sweeps_expired_clips_and_reports_which()
    {
        var expired = Secret("token");
        var kept = TestClips.Text("kept", Now);
        var store = new ClipStore(await OpenAsync());
        await store.SaveAsync(expired, Ct);
        await store.SaveAsync(kept, Ct);
        Clock.Advance(_twoMinutes);

        var swept = await store.SweepExpiredAsync(Ct);

        swept.IsSuccess.ShouldBeTrue();
        swept.Value.ShouldBe([expired.Id]);
        (await store.LoadAsync(Ct)).Value.Clips.Select(clip => clip.Id).ShouldBe([kept.Id]);
    }
}
