using ClipBench.Core.Boards;
using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Snippets;
using ClipBench.Storage.Boards;
using ClipBench.Storage.Clips;
using ClipBench.Storage.Snippets;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests.Clips;

public class ClipStoreTests : RepositoryTestBase
{
    private async Task<ClipStore> OpenStoreAsync() => new(await OpenAsync());

    [Fact]
    public async Task What_was_saved_is_loaded_newest_first_in_every_format()
    {
        var store = await OpenStoreAsync();
        var clips = TestClips.OfEveryFormat();
        foreach (var clip in clips)
        {
            (await store.SaveAsync(clip, Ct)).IsSuccess.ShouldBeTrue();
        }

        var loaded = await store.LoadAsync(Ct);

        loaded.IsSuccess.ShouldBeTrue();
        loaded.Value.UnreadableCount.ShouldBe(0);
        loaded.Value.Clips.ShouldBe(clips.Reverse());
    }

    [Fact]
    public async Task Wiping_through_the_store_deletes_clips_boards_and_snippets_and_the_store_still_works()
    {
        var repository = await OpenAsync();
        var store = new ClipStore(repository);
        var clips = TestClips.OfEveryFormat();
        foreach (var clip in clips)
        {
            await store.SaveAsync(clip, Ct);
        }

        await repository.SetPinnedAsync(clips[0].Id, true, Ct);
        await new BoardStore(repository).AddAsync(new Board(BoardId.New(), "Work", 0), Ct);
        await new SnippetStore(repository).SaveAsync(new Snippet(SnippetId.New(), "Signature", "Kind regards", 0), Ct);

        var wiped = await ((IStoreWipe)store).WipeAsync(Ct);

        wiped.IsSuccess.ShouldBeTrue();
        (await store.LoadAsync(Ct)).Value.Clips.ShouldBeEmpty();
        (await new BoardStore(repository).LoadAsync(Ct)).Value.ShouldBeEmpty();
        (await new SnippetStore(repository).LoadAsync(Ct)).Value.ShouldBeEmpty();

        var fresh = TestClips.Text("after the wipe");
        (await store.SaveAsync(fresh, Ct)).IsSuccess.ShouldBeTrue();
        (await store.LoadAsync(Ct)).Value.Clips.ShouldBe([fresh]);
    }

    [Fact]
    public async Task A_new_store_over_an_empty_database_loads_nothing()
    {
        var store = await OpenStoreAsync();

        var loaded = await store.LoadAsync(Ct);

        loaded.IsSuccess.ShouldBeTrue();
        loaded.Value.Clips.ShouldBeEmpty();
        loaded.Value.UnreadableCount.ShouldBe(0);
    }

    [Fact]
    public async Task Saving_a_clip_again_replaces_it()
    {
        var store = await OpenStoreAsync();
        var clip = TestClips.Text("same id");
        await store.SaveAsync(clip, Ct);

        await store.SaveAsync(clip with { IsPinned = true }, Ct);

        var loaded = (await store.LoadAsync(Ct)).Value;
        loaded.Clips.Length.ShouldBe(1);
        loaded.Clips[0].IsPinned.ShouldBeTrue();
    }

    [Fact]
    public async Task A_pin_is_kept_and_can_be_cleared()
    {
        var store = await OpenStoreAsync();
        var clip = TestClips.Text("pin me");
        await store.SaveAsync(clip, Ct);

        (await store.SetPinnedAsync(clip.Id, pinned: true, Ct)).IsSuccess.ShouldBeTrue();
        (await store.LoadAsync(Ct)).Value.Clips.Single().IsPinned.ShouldBeTrue();

        (await store.SetPinnedAsync(clip.Id, pinned: false, Ct)).IsSuccess.ShouldBeTrue();
        (await store.LoadAsync(Ct)).Value.Clips.Single().IsPinned.ShouldBeFalse();
    }

    [Fact]
    public async Task Pinning_a_clip_that_is_not_stored_fails_with_clip_not_found()
    {
        var store = await OpenStoreAsync();

        var result = await store.SetPinnedAsync(ClipId.New(), pinned: true, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.ClipNotFound);
    }

    [Fact]
    public async Task Deleted_clips_are_gone_and_ids_that_were_never_stored_are_ignored()
    {
        var store = await OpenStoreAsync();
        var gone = TestClips.Text("gone");
        var stays = TestClips.Text("stays");
        await store.SaveAsync(gone, Ct);
        await store.SaveAsync(stays, Ct);

        var result = await store.DeleteAsync([gone.Id, ClipId.New()], Ct);

        result.IsSuccess.ShouldBeTrue();
        (await store.LoadAsync(Ct)).Value.Clips.ShouldBe([stays]);
    }

    [Fact]
    public async Task A_record_that_fails_its_integrity_check_is_counted_and_the_others_still_load()
    {
        var store = await OpenStoreAsync();
        var damaged = TestClips.Text("damaged");
        var fine = TestClips.Text("fine");
        await store.SaveAsync(damaged, Ct);
        await store.SaveAsync(fine, Ct);
        var content = RawBlob("content", damaged.Id);
        content[^1] ^= 0xFF;
        RawSet("content", damaged.Id, content);

        var loaded = (await store.LoadAsync(Ct)).Value;

        loaded.Clips.ShouldBe([fine]);
        loaded.UnreadableCount.ShouldBe(1);
    }
}
