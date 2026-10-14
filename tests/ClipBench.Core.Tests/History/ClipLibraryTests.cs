using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Privacy;
using ClipBench.Core.Results;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.History;

public class ClipLibraryTests
{
    private const string Jwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

    private static readonly ResultError _storeError = new(ErrorCode.StorageFailure, "the disk said no");

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeClipStore _store = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ClipLibrary Create(int maxCount = 1000) =>
        new(new ClipHistory(new HistoryOptions { MaxCount = maxCount }, _clock), _store);

    private static string[] Texts(ClipLibrary library) =>
        [.. library.Clips.Select(clip => ((TextContent)clip.Content).Text)];

    // --- loading ---

    [Fact]
    public async Task Loading_puts_the_stored_clips_in_the_library_newest_first()
    {
        _store.Stored = [TestClips.Text("newest"), TestClips.Text("middle"), TestClips.Text("oldest")];
        var library = Create();

        var load = await library.LoadAsync(Ct);

        load.IsSuccess.ShouldBeTrue();
        load.Value.ShouldBe(new LibraryLoad(3, 0));
        Texts(library).ShouldBe(["newest", "middle", "oldest"]);
    }

    [Fact]
    public async Task Loading_reports_the_records_that_could_not_be_read()
    {
        _store.Stored = [TestClips.Text("one")];
        _store.Unreadable = 2;
        var library = Create();

        (await library.LoadAsync(Ct)).Value.ShouldBe(new LibraryLoad(1, 2));
    }

    [Fact]
    public async Task Loading_raises_changed_once()
    {
        _store.Stored = [TestClips.Text("one"), TestClips.Text("two")];
        var library = Create();
        var raised = 0;
        library.Changed += (_, _) => raised++;

        await library.LoadAsync(Ct);

        raised.ShouldBe(1);
    }

    [Fact]
    public async Task Loading_deletes_the_stored_clips_the_limits_push_out()
    {
        var newest = TestClips.Text("newest");
        var middle = TestClips.Text("middle");
        var oldest = TestClips.Text("oldest");
        _store.Stored = [newest, middle, oldest];
        var library = Create(maxCount: 2);

        var load = await library.LoadAsync(Ct);

        Texts(library).ShouldBe(["newest", "middle"]);
        _store.Deleted.ShouldBe([oldest.Id]);
        load.Value.Loaded.ShouldBe(2);
    }

    [Fact]
    public async Task A_store_that_cannot_be_read_leaves_the_library_empty_and_says_why()
    {
        _store.Failure = _storeError;
        var library = Create();

        var load = await library.LoadAsync(Ct);

        load.IsFailure.ShouldBeTrue();
        load.Error.ShouldBe(_storeError);
        library.Clips.ShouldBeEmpty();
    }

    // --- adding ---

    [Fact]
    public async Task Adding_puts_the_clip_on_top_stores_it_and_raises_changed()
    {
        var library = Create();
        await library.AddAsync(TestClips.Text("first"), Ct);
        var raised = 0;
        library.Changed += (_, _) => raised++;
        var second = TestClips.Text("second");

        var added = await library.AddAsync(second, Ct);

        added.IsSuccess.ShouldBeTrue();
        added.Value.WasDuplicate.ShouldBeFalse();
        Texts(library).ShouldBe(["second", "first"]);
        _store.Saved.Select(clip => clip.Id).ShouldContain(second.Id);
        raised.ShouldBe(1);
    }

    [Fact]
    public async Task Copying_the_same_content_again_moves_it_to_the_top_and_stores_the_existing_clip()
    {
        var library = Create();
        var original = TestClips.Text("same");
        await library.AddAsync(original, Ct);
        await library.AddAsync(TestClips.Text("other"), Ct);

        var added = await library.AddAsync(TestClips.Text("same", capturedAt: TestClips.Start.AddMinutes(5)), Ct);

        added.Value.WasDuplicate.ShouldBeTrue();
        Texts(library).ShouldBe(["same", "other"]);
        added.Value.Clip.Id.ShouldBe(original.Id);
        _store.Saved[^1].Id.ShouldBe(original.Id);
        _store.Saved[^1].CapturedAt.ShouldBe(TestClips.Start.AddMinutes(5));
    }

    [Fact]
    public async Task Adding_deletes_the_stored_clips_the_limits_push_out()
    {
        var library = Create(maxCount: 2);
        var oldest = TestClips.Text("oldest");
        await library.AddAsync(oldest, Ct);
        await library.AddAsync(TestClips.Text("middle"), Ct);

        await library.AddAsync(TestClips.Text("newest"), Ct);

        Texts(library).ShouldBe(["newest", "middle"]);
        _store.Deleted.ShouldBe([oldest.Id]);
    }

    [Fact]
    public async Task A_clip_that_could_not_be_stored_stays_in_the_library_and_the_failure_is_returned()
    {
        var library = Create();
        _store.Failure = _storeError;
        var raised = 0;
        library.Changed += (_, _) => raised++;

        var added = await library.AddAsync(TestClips.Text("kept in memory"), Ct);

        added.IsFailure.ShouldBeTrue();
        added.Error.ShouldBe(_storeError);
        Texts(library).ShouldBe(["kept in memory"]);
        raised.ShouldBe(1);
    }

    [Fact]
    public async Task Adds_that_arrive_together_are_stored_one_at_a_time()
    {
        var library = Create();
        var firstSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.DuringSave = async () =>
        {
            firstSaveStarted.TrySetResult();
            await release.Task;
        };

        var first = library.AddAsync(TestClips.Text("one"), Ct);
        await firstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var second = library.AddAsync(TestClips.Text("two"), Ct);

        // If the second add were allowed to start its save now, it would overlap the first. Give it the chance.
        await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(200), Ct));
        release.SetResult();
        await Task.WhenAll(first, second);

        _store.MaxConcurrentSaves.ShouldBe(1);
        _store.Saved.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_subscriber_that_throws_does_not_stop_the_others_or_undo_the_change()
    {
        var library = Create();
        var told = 0;
        library.Changed += (_, _) => throw new InvalidOperationException("a subscriber bug");
        library.Changed += (_, _) => told++;

        var added = await library.AddAsync(TestClips.Text("still added"), Ct);

        added.IsSuccess.ShouldBeTrue();
        told.ShouldBe(1);
        Texts(library).ShouldBe(["still added"]);
    }

    // --- pinning ---

    [Fact]
    public async Task Pinning_marks_the_clip_stores_the_pin_and_raises_changed()
    {
        var library = Create();
        var clip = TestClips.Text("keep");
        await library.AddAsync(clip, Ct);
        var raised = 0;
        library.Changed += (_, _) => raised++;

        var result = await library.SetPinnedAsync(clip.Id, pinned: true, Ct);

        result.IsSuccess.ShouldBeTrue();
        library.Clips.Single().IsPinned.ShouldBeTrue();
        _store.PinChanges.ShouldBe([(clip.Id, true)]);
        raised.ShouldBe(1);
    }

    [Fact]
    public async Task Unpinning_clears_the_pin_in_memory_and_in_the_store()
    {
        var library = Create();
        var clip = TestClips.Text("keep", pinned: true);
        await library.AddAsync(clip, Ct);

        await library.SetPinnedAsync(clip.Id, pinned: false, Ct);

        library.Clips.Single().IsPinned.ShouldBeFalse();
        _store.PinChanges.ShouldBe([(clip.Id, false)]);
    }

    [Fact]
    public async Task Pinning_a_clip_that_is_not_in_the_library_fails_and_touches_nothing()
    {
        var library = Create();
        var raised = 0;
        library.Changed += (_, _) => raised++;

        var result = await library.SetPinnedAsync(ClipId.New(), pinned: true, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.ClipNotFound);
        _store.PinChanges.ShouldBeEmpty();
        raised.ShouldBe(0);
    }

    [Fact]
    public async Task A_pin_that_could_not_be_stored_is_returned_as_a_failure_and_stays_in_memory()
    {
        var library = Create();
        var clip = TestClips.Text("keep");
        await library.AddAsync(clip, Ct);
        _store.Failure = _storeError;

        var result = await library.SetPinnedAsync(clip.Id, pinned: true, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_storeError);
        library.Clips.Single().IsPinned.ShouldBeTrue();
    }

    // --- removing ---

    [Fact]
    public async Task Removing_takes_the_clip_out_deletes_it_from_the_store_and_raises_changed()
    {
        var library = Create();
        var gone = TestClips.Text("gone");
        await library.AddAsync(gone, Ct);
        await library.AddAsync(TestClips.Text("stays"), Ct);
        var raised = 0;
        library.Changed += (_, _) => raised++;

        var result = await library.RemoveAsync(gone.Id, Ct);

        result.IsSuccess.ShouldBeTrue();
        Texts(library).ShouldBe(["stays"]);
        _store.Deleted.ShouldBe([gone.Id]);
        raised.ShouldBe(1);
    }

    [Fact]
    public async Task A_pinned_clip_can_be_removed_too()
    {
        var library = Create();
        var pinned = TestClips.Text("pinned", pinned: true);
        await library.AddAsync(pinned, Ct);

        (await library.RemoveAsync(pinned.Id, Ct)).IsSuccess.ShouldBeTrue();

        library.Clips.ShouldBeEmpty();
    }

    [Fact]
    public async Task Removing_a_clip_that_is_not_in_the_library_fails_and_touches_nothing()
    {
        var library = Create();

        var result = await library.RemoveAsync(ClipId.New(), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.ClipNotFound);
        _store.Deleted.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_removal_that_could_not_be_stored_is_returned_as_a_failure_and_stays_in_memory()
    {
        var library = Create();
        var clip = TestClips.Text("gone");
        await library.AddAsync(clip, Ct);
        _store.Failure = _storeError;

        var result = await library.RemoveAsync(clip.Id, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_storeError);
        library.Clips.ShouldBeEmpty();
    }

    // --- searching ---

    [Fact]
    public async Task Searching_finds_clips_by_their_text()
    {
        var library = Create();
        await library.AddAsync(TestClips.Text("invoice for March"), Ct);
        await library.AddAsync(TestClips.Text("holiday photos"), Ct);

        var results = library.Search("invoice");

        results.Select(result => result.Clip.Content.PlainText).ShouldBe(["invoice for March"]);
    }

    [Fact]
    public async Task An_empty_search_returns_the_most_recent_clips_up_to_the_limit()
    {
        var library = Create();
        await library.AddAsync(TestClips.Text("one"), Ct);
        await library.AddAsync(TestClips.Text("two"), Ct);
        await library.AddAsync(TestClips.Text("three"), Ct);

        var results = library.Search("", limit: 2);

        results.Select(result => result.Clip.Content.PlainText).ShouldBe(["three", "two"]);
    }

    // --- sensitivity ---

    [Fact]
    public void A_token_is_reported_as_sensitive()
    {
        Create().GetSensitivity(TestClips.Text(Jwt)).ShouldBe(SensitiveKind.Jwt);
    }

    [Fact]
    public void Ordinary_text_and_images_are_not_sensitive()
    {
        var library = Create();

        library.GetSensitivity(TestClips.Text("see you at noon")).ShouldBe(SensitiveKind.None);
        library.GetSensitivity(TestClips.Image(1)).ShouldBe(SensitiveKind.None);
    }

    [Fact]
    public void A_file_list_is_never_reported_as_a_secret_even_when_a_folder_name_looks_like_a_key()
    {
        // A temporary folder named after a GUID looks like a long random token to the detector. A path is not a secret.
        var folder = @"C:\Users\someone\AppData\Local\Temp\clipbench-visual2-files-7c4e1d9a3b6f4f2e8a15d0c9b7e3a621";
        var files = TestClips.Files(paths: [folder + @"\quarterly-report.xlsx", folder + @"\notes.txt", folder + @"\photo.jpg"]);

        Create().GetSensitivity(files).ShouldBe(SensitiveKind.None);
    }
}
