using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Capture;

public sealed class CollectControllerTests : IDisposable
{
    private readonly FakeTimeProvider _clock = new();
    private readonly FakeClipStore _store = new();
    private readonly FakeClipboardWriter _writer = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeActivityLog _log = new();
    private readonly SettingsService _settings = new(new FakeSettingsStore());
    private readonly ClipLibrary _library;
    private readonly CollectController _collect;

    public CollectControllerTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _clock), _store);
        _collect = Create();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _library.Dispose();
        _settings.Dispose();
    }

    private CollectController Create(long maxBytes = CollectSession.DefaultMaxBytes) =>
        new(_library, _writer, _settings, _clock, _notifier, _log, maxBytes);

    private static Clip Copy(string text) => TestClips.Text(text);

    private string[] Texts() => [.. _library.Clips.Select(clip => ((TextContent)clip.Content).Text)];

    // --- before a session ---

    [Fact]
    public void Without_a_session_every_copy_goes_to_the_history()
    {
        _collect.IsActive.ShouldBeFalse();

        _collect.Offer(Copy("plain")).ShouldBeTrue();

        _collect.Count.ShouldBe(0);
    }

    // --- collecting ---

    [Fact]
    public void Starting_a_session_is_announced_and_logged()
    {
        var raised = 0;
        _collect.Changed += (_, _) => raised++;

        _collect.Start();

        _collect.IsActive.ShouldBeTrue();
        raised.ShouldBe(1);
        _log.Entries.Single().Activity.ShouldBe(ActivityEvent.CollectStarted);
    }

    [Fact]
    public void During_a_session_each_text_copy_is_collected_and_kept_out_of_the_history()
    {
        _collect.Start();
        var raised = 0;
        _collect.Changed += (_, _) => raised++;

        _collect.Offer(Copy("first quote")).ShouldBeFalse();
        _collect.Offer(Copy("second quote")).ShouldBeFalse();

        _collect.Count.ShouldBe(2);
        raised.ShouldBe(2);
    }

    [Fact]
    public async Task With_keep_copies_on_each_copy_is_collected_and_also_kept()
    {
        await _settings.UpdateAsync(settings => settings with { KeepCopiesWhileCollecting = true }, Ct);
        _collect.Start();

        _collect.Offer(Copy("both")).ShouldBeTrue();

        _collect.Count.ShouldBe(1);
    }

    [Fact]
    public void A_picture_is_not_collected_and_goes_to_the_history()
    {
        _collect.Start();

        _collect.Offer(TestClips.Image(1)).ShouldBeTrue();

        _collect.Count.ShouldBe(0);
    }

    [Fact]
    public void A_copy_past_the_size_limit_goes_to_the_history_and_the_user_is_told_once()
    {
        var collect = Create(maxBytes: 10);
        collect.Start();

        collect.Offer(Copy("12345")).ShouldBeFalse();
        collect.Offer(Copy("too long now")).ShouldBeTrue();
        collect.Offer(Copy("also too long")).ShouldBeTrue();

        collect.Count.ShouldBe(1);
        _notifier.Shown.ShouldBe([("ClipBench", "The collected text reached its size limit. Further copies are saved as separate clips.")]);
        _log.Entries.Count(entry => entry.Activity == ActivityEvent.CollectLimitReached).ShouldBe(1);
    }

    [Fact]
    public void Starting_again_while_collecting_keeps_what_was_collected()
    {
        _collect.Start();
        _collect.Offer(Copy("kept"));

        _collect.Start();

        _collect.Count.ShouldBe(1);
        _log.Entries.Count(entry => entry.Activity == ActivityEvent.CollectStarted).ShouldBe(1);
    }

    // --- finishing ---

    [Fact]
    public async Task Finishing_saves_one_clip_at_the_top_of_the_history_and_puts_it_on_the_clipboard()
    {
        await _library.AddAsync(Copy("older"), Ct);
        _collect.Start();
        _collect.Offer(Copy("one"));
        _collect.Offer(Copy("two"));
        var raised = 0;
        _collect.Changed += (_, _) => raised++;

        var result = await _collect.FinishAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        Texts().ShouldBe(["one\r\ntwo", "older"]);
        ((TextContent)_writer.Written.Single()).Text.ShouldBe("one\r\ntwo");
        _collect.IsActive.ShouldBeFalse();
        _collect.Count.ShouldBe(0);
        raised.ShouldBe(1);
        var finished = _log.Entries.Single(entry => entry.Activity == ActivityEvent.CollectFinished);
        finished.Details.Count.ShouldBe(2);
        finished.Details.Bytes.ShouldBe(8);
    }

    [Fact]
    public async Task The_separator_chosen_when_the_session_started_joins_the_copies()
    {
        await _settings.UpdateAsync(settings => settings with { Separator = SeparatorChoice.BlankLine }, Ct);
        _collect.Start();
        await _settings.UpdateAsync(settings => settings with { Separator = SeparatorChoice.Comma }, Ct);
        _collect.Offer(Copy("one"));
        _collect.Offer(Copy("two"));

        await _collect.FinishAsync(Ct);

        Texts().ShouldBe(["one\r\n\r\ntwo"]);
    }

    [Fact]
    public async Task Finishing_a_session_that_collected_nothing_saves_nothing()
    {
        _collect.Start();

        var result = await _collect.FinishAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        _library.Clips.ShouldBeEmpty();
        _writer.Written.ShouldBeEmpty();
        _collect.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Finishing_without_a_session_does_nothing()
    {
        var raised = 0;
        _collect.Changed += (_, _) => raised++;

        (await _collect.FinishAsync(Ct)).IsSuccess.ShouldBeTrue();

        raised.ShouldBe(0);
        _log.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_the_history_cannot_save_it_the_clip_still_reaches_the_clipboard_and_the_user_is_told()
    {
        _store.Failure = new ResultError(ErrorCode.StorageFailure, "disk full");
        _collect.Start();
        _collect.Offer(Copy("text"));

        var result = await _collect.FinishAsync(Ct);

        result.Error.Code.ShouldBe(ErrorCode.StorageFailure);
        _writer.Written.ShouldHaveSingleItem();
        _notifier.Shown.ShouldBe([("ClipBench", "The collected text is on your clipboard, but it could not be saved in the history.")]);
        _log.Entries.ShouldContain(entry => entry.Activity == ActivityEvent.CollectFailed && entry.Details.Error == ErrorCode.StorageFailure);
    }

    [Fact]
    public async Task When_the_clipboard_cannot_take_it_the_clip_is_still_saved_and_the_user_is_told()
    {
        _writer.Failure = new ResultError(ErrorCode.ClipboardBusy, "busy");
        _collect.Start();
        _collect.Offer(Copy("text"));

        var result = await _collect.FinishAsync(Ct);

        result.Error.Code.ShouldBe(ErrorCode.ClipboardBusy);
        Texts().ShouldBe(["text"]);
        _notifier.Shown.ShouldBe([("ClipBench", "The collected text is saved in your history, but ClipBench could not put it on the clipboard.")]);
    }

    [Fact]
    public async Task When_neither_the_history_nor_the_clipboard_takes_it_the_user_is_told_both()
    {
        _store.Failure = new ResultError(ErrorCode.StorageFailure, "disk full");
        _writer.Failure = new ResultError(ErrorCode.ClipboardBusy, "busy");
        _collect.Start();
        _collect.Offer(Copy("text"));

        var result = await _collect.FinishAsync(Ct);

        result.Error.Code.ShouldBe(ErrorCode.StorageFailure);
        _notifier.Shown.ShouldBe([("ClipBench", "The collected text could not be saved in the history or put on the clipboard.")]);
    }

    [Fact]
    public async Task After_finishing_copies_go_to_the_history_again()
    {
        _collect.Start();
        _collect.Offer(Copy("in"));
        await _collect.FinishAsync(Ct);

        _collect.Offer(Copy("after")).ShouldBeTrue();
    }

    // --- cancelling ---

    [Fact]
    public void Cancelling_throws_the_collected_text_away()
    {
        _collect.Start();
        _collect.Offer(Copy("gone"));
        var raised = 0;
        _collect.Changed += (_, _) => raised++;

        _collect.Cancel();

        _collect.IsActive.ShouldBeFalse();
        _collect.Count.ShouldBe(0);
        _library.Clips.ShouldBeEmpty();
        raised.ShouldBe(1);
        _log.Entries[^1].Activity.ShouldBe(ActivityEvent.CollectCancelled);
    }

    [Fact]
    public void Cancelling_without_a_session_does_nothing()
    {
        var raised = 0;
        _collect.Changed += (_, _) => raised++;

        _collect.Cancel();

        raised.ShouldBe(0);
        _log.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void A_subscriber_that_throws_does_not_stop_collecting()
    {
        _collect.Changed += (_, _) => throw new InvalidOperationException("subscriber bug");
        _collect.Start();

        _collect.Offer(Copy("still collected")).ShouldBeFalse();

        _collect.Count.ShouldBe(1);
    }

    [Fact]
    public void Missing_services_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new CollectController(null!, _writer, _settings, _clock, _notifier, _log));
        Should.Throw<ArgumentNullException>(() => new CollectController(_library, null!, _settings, _clock, _notifier, _log));
        Should.Throw<ArgumentNullException>(() => new CollectController(_library, _writer, null!, _clock, _notifier, _log));
        Should.Throw<ArgumentNullException>(() => new CollectController(_library, _writer, _settings, null!, _notifier, _log));
        Should.Throw<ArgumentNullException>(() => new CollectController(_library, _writer, _settings, _clock, null!, _log));
        Should.Throw<ArgumentNullException>(() => new CollectController(_library, _writer, _settings, _clock, _notifier, null!));
        Should.Throw<ArgumentNullException>(() => _collect.Offer(null!));
    }
}
