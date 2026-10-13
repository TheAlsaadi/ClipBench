using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Capture;

public sealed class CaptureServiceTests : IDisposable
{
    private readonly FakeClipboardListener _listener = new();
    private readonly FakeClipboardReader _reader = new();
    private readonly FakeActivityLog _log = new();
    private readonly FakeClipStore _store = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly ClipLibrary _library;
    private readonly CaptureService _service;

    public CaptureServiceTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _clock), _store);
        _service = new CaptureService(_listener, _reader, _library, _log, _clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        _library.Dispose();
    }

    private static Result<ClipboardCapture> Copied(long sequence, string text, string? source = "notepad.exe") =>
        Result.Success(ClipboardCapture.Accepted(sequence, new TextContent(text), source));

    private async Task StartAsync() => (await _service.StartAsync(Ct)).IsSuccess.ShouldBeTrue();

    private static string[] Texts(ClipLibrary library) =>
        [.. library.Clips.Select(clip => ((TextContent)clip.Content).Text)];

    // --- starting ---

    [Fact]
    public async Task Starting_starts_the_listener()
    {
        var result = await _service.StartAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        _listener.StartCalls.ShouldBe(1);
    }

    [Fact]
    public async Task A_listener_that_cannot_start_is_returned_and_logged()
    {
        var failure = new ResultError(ErrorCode.PlatformFailure, "no listener");
        _listener.StartResult = Result.Failure(failure);

        var result = await _service.StartAsync(Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(failure);
        var entry = _log.Entries.Single();
        entry.Level.ShouldBe(ActivityLevel.Error);
        entry.Activity.ShouldBe(ActivityEvent.CaptureListenFailed);
        entry.Details.Error.ShouldBe(ErrorCode.PlatformFailure);
    }

    [Fact]
    public async Task A_listener_that_started_is_logged()
    {
        await StartAsync();

        _log.Entries.Single().Activity.ShouldBe(ActivityEvent.CaptureListening);
    }

    [Fact]
    public async Task A_start_that_failed_can_be_tried_again_and_then_works()
    {
        _listener.StartResult = Result.Failure(new ResultError(ErrorCode.PlatformFailure, "not yet"));
        (await _service.StartAsync(Ct)).IsFailure.ShouldBeTrue();
        _listener.HasSubscribers.ShouldBeFalse("a service that is not listening must not hold the listener");

        _listener.StartResult = Result.Success();
        (await _service.StartAsync(Ct)).IsSuccess.ShouldBeTrue();
        _reader.Returns(Copied(1, "after the retry"));
        _listener.Raise(1);
        await _log.WaitForCountAsync(3, Ct);

        _listener.StartCalls.ShouldBe(2);
        Texts(_library).ShouldBe(["after the retry"]);
    }

    [Fact]
    public async Task Starting_twice_listens_once()
    {
        await StartAsync();
        await StartAsync();

        _listener.StartCalls.ShouldBe(1);
        _log.Entries.Count.ShouldBe(1);
    }

    // --- recording ---

    [Fact]
    public async Task A_change_is_read_and_stored_with_its_source_and_the_time_it_was_copied()
    {
        await StartAsync();
        _reader.Returns(Copied(1, "hello", "winword.exe"));
        _clock.Advance(TimeSpan.FromMinutes(5));

        _listener.Raise(1);
        await _log.WaitForCountAsync(2, Ct);

        var clip = _library.Clips.Single();
        clip.Content.ShouldBe(new TextContent("hello"));
        clip.SourceApp.ShouldBe("winword.exe");
        clip.CapturedAt.ShouldBe(_clock.GetUtcNow());
        _store.Saved.Single().Id.ShouldBe(clip.Id);
    }

    [Fact]
    public async Task A_recorded_change_is_logged_by_kind_and_size_only()
    {
        await StartAsync();
        _reader.Returns(Copied(1, "twelve bytes"));

        _listener.Raise(1);
        await _log.WaitForCountAsync(2, Ct);

        var entry = _log.Entries[1];
        entry.Level.ShouldBe(ActivityLevel.Information);
        entry.Activity.ShouldBe(ActivityEvent.CaptureStored);
        entry.Details.Format.ShouldBe(ClipFormat.Text);
        entry.Details.Bytes.ShouldBe(12);
    }

    [Fact]
    public async Task A_change_the_filter_rejects_is_not_stored_and_the_reason_is_logged()
    {
        await StartAsync();
        _reader.Returns(Result.Success(ClipboardCapture.Rejected(1, CaptureRejection.PrivacyMarker)));

        _listener.Raise(1);
        await _log.WaitForCountAsync(2, Ct);

        _library.Clips.ShouldBeEmpty();
        var entry = _log.Entries[1];
        entry.Activity.ShouldBe(ActivityEvent.CaptureRejected);
        entry.Details.Rejection.ShouldBe(CaptureRejection.PrivacyMarker);
    }

    [Theory]
    [InlineData(ErrorCode.ClipboardEmpty)]
    [InlineData(ErrorCode.ClipboardBusy)]
    [InlineData(ErrorCode.ClipboardUnreadable)]
    public async Task A_clipboard_that_cannot_be_read_stores_nothing_and_the_error_is_logged(ErrorCode code)
    {
        await StartAsync();
        _reader.Returns(Result.Failure<ClipboardCapture>(new ResultError(code, "details")));

        _listener.Raise(1);
        await _log.WaitForCountAsync(2, Ct);

        _library.Clips.ShouldBeEmpty();
        var entry = _log.Entries[1];
        entry.Activity.ShouldBe(ActivityEvent.CaptureFailed);
        entry.Details.Error.ShouldBe(code);
    }

    [Fact]
    public async Task A_clip_that_could_not_be_saved_stays_in_the_library_and_the_error_is_logged()
    {
        await StartAsync();
        _store.Failure = new ResultError(ErrorCode.StorageFailure, "disk full");
        _reader.Returns(Copied(1, "kept"));

        _listener.Raise(1);
        await _log.WaitForCountAsync(2, Ct);

        Texts(_library).ShouldBe(["kept"]);
        var entry = _log.Entries[1];
        entry.Level.ShouldBe(ActivityLevel.Error);
        entry.Activity.ShouldBe(ActivityEvent.StoreFailed);
        entry.Details.Error.ShouldBe(ErrorCode.StorageFailure);
    }

    // --- the filter ---

    [Fact]
    public async Task Pausing_makes_the_next_read_use_a_filter_that_rejects_everything_and_resuming_undoes_it()
    {
        await StartAsync();
        _reader.Returns(Copied(1, "one"));
        _service.IsPaused.ShouldBeFalse();

        _service.SetPaused(true);
        _listener.Raise(1);
        await _log.WaitForCountAsync(2, Ct);

        _service.IsPaused.ShouldBeTrue();
        _reader.Filters.Last().Incognito.ShouldBeTrue();

        _service.SetPaused(false);
        _reader.Returns(Copied(2, "two"));
        _listener.Raise(2);
        await _log.WaitForCountAsync(3, Ct);

        _service.IsPaused.ShouldBeFalse();
        _reader.Filters.Last().Incognito.ShouldBeFalse();
    }

    [Fact]
    public async Task A_filter_update_applies_to_the_next_read_and_keeps_the_other_rules()
    {
        await StartAsync();
        _service.SetPaused(true);
        _reader.Returns(Copied(1, "one"));

        _service.UpdateFilter(filter => filter with { ExcludedProcesses = ["keepass.exe"] });
        _listener.Raise(1);
        await _log.WaitForCountAsync(2, Ct);

        var used = _reader.Filters.Single();
        used.ExcludedProcesses.ShouldBe(["keepass.exe"]);
        used.Incognito.ShouldBeTrue();
        _service.Filter.ShouldBe(used);
    }

    [Fact]
    public void The_filter_given_at_creation_is_the_one_in_force()
    {
        var custom = new CaptureFilter { ExcludedProcesses = ["bitwarden.exe"] };
        using var service = new CaptureService(_listener, _reader, _library, _log, _clock, custom);

        service.Filter.ShouldBe(custom);
    }

    // --- ordering ---

    [Fact]
    public async Task Changes_are_handled_one_at_a_time_in_the_order_they_arrived()
    {
        await StartAsync();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _reader.Handler = async (_, _) =>
        {
            if (_reader.Calls == 1)
            {
                firstStarted.TrySetResult();
                await release.Task;
                return Copied(1, "first");
            }

            return Copied(2, "second");
        };

        _listener.Raise(1);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        _listener.Raise(2);

        // If the second change were handled now it would overlap the first. Give it the chance.
        await Task.Delay(TimeSpan.FromMilliseconds(200), Ct);
        release.SetResult();
        await _log.WaitForCountAsync(3, Ct);

        _reader.MaxConcurrentReads.ShouldBe(1);
        Texts(_library).ShouldBe(["second", "first"]);
    }

    [Fact]
    public async Task A_change_that_an_earlier_read_already_covered_is_not_read_again()
    {
        await StartAsync();

        // The first read happens late: the clipboard has already moved on to number 10.
        _reader.Returns(Copied(10, "latest"));
        _listener.Raise(1);
        await _log.WaitForCountAsync(2, Ct);

        _listener.Raise(5);
        _listener.Raise(10);
        _reader.Returns(Copied(11, "newer"));
        _listener.Raise(11);
        await _log.WaitForCountAsync(3, Ct);

        _reader.Calls.ShouldBe(2);
        Texts(_library).ShouldBe(["newer", "latest"]);
    }

    // --- faults ---

    [Fact]
    public async Task A_reader_that_throws_is_logged_by_type_and_the_next_change_is_still_handled()
    {
        await StartAsync();
        _reader.Handler = (_, _) => throw new InvalidOperationException("secret text in a message");
        _listener.Raise(1);
        await _log.WaitForCountAsync(2, Ct);

        _reader.Returns(Copied(2, "after the fault"));
        _listener.Raise(2);
        await _log.WaitForCountAsync(3, Ct);

        var fault = _log.Entries[1];
        fault.Level.ShouldBe(ActivityLevel.Error);
        fault.Activity.ShouldBe(ActivityEvent.CaptureFaulted);
        fault.Details.ExceptionType.ShouldBe(typeof(InvalidOperationException));
        Texts(_library).ShouldBe(["after the fault"]);
    }

    // --- stopping ---

    [Fact]
    public async Task After_dispose_changes_are_ignored_and_the_listener_is_not_read_from()
    {
        await StartAsync();
        _service.Dispose();

        _listener.Raise(1);

        _listener.HasSubscribers.ShouldBeFalse();
        _reader.Calls.ShouldBe(0);
        _library.Clips.ShouldBeEmpty();
    }

    [Fact]
    public void Disposing_twice_is_fine()
    {
        _service.Dispose();

        Should.NotThrow(_service.Dispose);
    }

    [Fact]
    public void A_service_that_was_never_started_does_not_listen()
    {
        _listener.HasSubscribers.ShouldBeFalse();
    }
}
