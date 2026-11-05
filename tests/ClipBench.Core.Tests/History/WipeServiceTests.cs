using ClipBench.Core.Boards;
using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.History;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Core.Snippets;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.History;

public sealed class WipeServiceTests : IDisposable
{
    private static readonly ResultError _storeError = new(ErrorCode.StorageFailure, "the disk said no");

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeClipStore _clipStore = new();
    private readonly FakeBoardStore _boardStore = new();
    private readonly FakeSnippetStore _snippetStore = new();
    private readonly FakeStoreWipe _wipe = new();
    private readonly FakeActivityLog _log = new();
    private readonly FakeCollect _collect = new();
    private readonly FakeQueue _queue = new();
    private readonly ClipLibrary _library;
    private readonly BoardCatalog _boards;
    private readonly SnippetCatalog _snippets;
    private readonly WipeService _service;

    public WipeServiceTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _clock), _clipStore);
        _boards = new BoardCatalog(_library, _boardStore);
        _snippets = new SnippetCatalog(_snippetStore);
        _service = new WipeService(_wipe, _library, _boards, _snippets, _collect, _queue, _log);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _snippets.Dispose();
        _boards.Dispose();
        _library.Dispose();
    }

    private async Task FillAsync()
    {
        await _library.AddAsync(TestClips.Text("a clip"), Ct);
        (await _boards.CreateAsync("Work", Ct)).IsSuccess.ShouldBeTrue();
        (await _snippets.CreateAsync("Signature", "Kind regards", Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Wiping_removes_the_clips_the_boards_and_the_snippets_from_the_store_and_from_memory()
    {
        await FillAsync();

        var result = await _service.WipeAllAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        _wipe.Wipes.ShouldBe(1);
        _library.Clips.ShouldBeEmpty();
        _boards.Boards.ShouldBeEmpty();
        _snippets.Snippets.ShouldBeEmpty();
    }

    [Fact]
    public async Task Wiping_throws_away_a_collect_session_and_stops_a_running_queue()
    {
        await FillAsync();

        await _service.WipeAllAsync(Ct);

        _collect.Cancels.ShouldBe(1);
        _queue.Stops.ShouldBe(1);
    }

    [Fact]
    public async Task Wiping_is_recorded_in_the_log_without_any_content()
    {
        await FillAsync();

        await _service.WipeAllAsync(Ct);

        _log.Entries.ShouldContain(entry => entry.Activity == ActivityEvent.HistoryWiped && entry.Level == ActivityLevel.Information);
    }

    [Fact]
    public async Task A_wipe_the_store_could_not_do_changes_nothing_in_memory_and_says_why()
    {
        await FillAsync();
        _wipe.Failure = _storeError;

        var result = await _service.WipeAllAsync(Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_storeError);
        _library.Clips.Length.ShouldBe(1);
        _boards.Boards.Length.ShouldBe(1);
        _snippets.Snippets.Length.ShouldBe(1);
        _log.Entries.ShouldContain(entry => entry.Activity == ActivityEvent.StoreFailed && entry.Details.Error == ErrorCode.StorageFailure);
        _log.Entries.ShouldNotContain(entry => entry.Activity == ActivityEvent.HistoryWiped);
    }

    [Fact]
    public async Task Even_a_failed_wipe_has_already_dropped_the_session_and_the_queue_because_they_hold_content()
    {
        _wipe.Failure = _storeError;

        await _service.WipeAllAsync(Ct);

        _collect.Cancels.ShouldBe(1);
        _queue.Stops.ShouldBe(1);
    }

    [Fact]
    public void Missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new WipeService(null!, _library, _boards, _snippets, _collect, _queue, _log));
        Should.Throw<ArgumentNullException>(() => new WipeService(_wipe, null!, _boards, _snippets, _collect, _queue, _log));
        Should.Throw<ArgumentNullException>(() => new WipeService(_wipe, _library, null!, _snippets, _collect, _queue, _log));
        Should.Throw<ArgumentNullException>(() => new WipeService(_wipe, _library, _boards, null!, _collect, _queue, _log));
        Should.Throw<ArgumentNullException>(() => new WipeService(_wipe, _library, _boards, _snippets, null!, _queue, _log));
        Should.Throw<ArgumentNullException>(() => new WipeService(_wipe, _library, _boards, _snippets, _collect, null!, _log));
        Should.Throw<ArgumentNullException>(() => new WipeService(_wipe, _library, _boards, _snippets, _collect, _queue, null!));
    }

    private sealed class FakeCollect : ICollectControl
    {
        public int Cancels { get; private set; }

        public bool IsActive => false;

        public int Count => 0;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public void Start()
        {
        }

        public Task<Result> FinishAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public void Cancel() => Cancels++;
    }

    private sealed class FakeQueue : IQueueControl
    {
        public int Stops { get; private set; }

        public QueueProgress Progress => default;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public Task<Result> StartAsync(IReadOnlyList<Clip> clips, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stops++;
            return Task.CompletedTask;
        }
    }
}
