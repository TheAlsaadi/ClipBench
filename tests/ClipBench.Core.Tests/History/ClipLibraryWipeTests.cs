using ClipBench.Core.Boards;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.History;

public sealed class ClipLibraryWipeTests : IDisposable
{
    private static readonly ResultError _storeError = new(ErrorCode.StorageFailure, "the disk said no");

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeClipStore _store = new();
    private readonly FakeStoreWipe _wipe = new();
    private readonly ClipLibrary _library;

    public ClipLibraryWipeTests() => _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _clock), _store);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _library.Dispose();

    [Fact]
    public async Task Wiping_empties_the_store_and_the_library_including_pinned_clips_and_clips_on_boards()
    {
        var first = (await _library.AddAsync(TestClips.Text("one"), Ct)).Value.Clip;
        var second = (await _library.AddAsync(TestClips.Text("two"), Ct)).Value.Clip;
        await _library.AddAsync(TestClips.Text("three"), Ct);
        await _library.SetPinnedAsync(first.Id, true, Ct);
        await _library.SetBoardAsync(second.Id, BoardId.New(), Ct);

        var result = await _library.WipeAsync(_wipe, Ct);

        result.IsSuccess.ShouldBeTrue();
        _wipe.Wipes.ShouldBe(1);
        _library.Clips.ShouldBeEmpty();
    }

    [Fact]
    public async Task Wiping_tells_the_listeners_once()
    {
        await _library.AddAsync(TestClips.Text("one"), Ct);
        var raised = 0;
        _library.Changed += (_, _) => raised++;

        await _library.WipeAsync(_wipe, Ct);

        raised.ShouldBe(1);
    }

    [Fact]
    public async Task A_wipe_the_store_could_not_do_keeps_the_clips_visible_and_reports_why()
    {
        await _library.AddAsync(TestClips.Text("still on disk"), Ct);
        _wipe.Failure = _storeError;
        var raised = 0;
        _library.Changed += (_, _) => raised++;

        var result = await _library.WipeAsync(_wipe, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_storeError);
        _library.Clips.Length.ShouldBe(1);
        raised.ShouldBe(0);
    }

    [Fact]
    public async Task Wiping_an_empty_library_succeeds()
    {
        (await _library.WipeAsync(_wipe, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_copy_that_is_being_saved_finishes_before_the_wipe_so_it_cannot_survive_it()
    {
        var release = new TaskCompletionSource();
        var saving = new TaskCompletionSource();
        _store.DuringSave = async () =>
        {
            saving.SetResult();
            await release.Task;
        };

        var add = _library.AddAsync(TestClips.Text("in flight"), Ct);
        await saving.Task;
        var wipe = _library.WipeAsync(_wipe, Ct);

        _wipe.Wipes.ShouldBe(0);
        release.SetResult();
        await add;
        await wipe;

        _wipe.Wipes.ShouldBe(1);
        _library.Clips.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_copy_after_the_wipe_is_kept()
    {
        await _library.WipeAsync(_wipe, Ct);

        await _library.AddAsync(TestClips.Text("after"), Ct);

        _library.Clips.Length.ShouldBe(1);
    }

    [Fact]
    public async Task A_missing_store_wipe_is_a_bug()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => _library.WipeAsync(null!, Ct));
    }
}
