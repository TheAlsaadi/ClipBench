using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Popup;

/// <summary>Ctrl+Q: the marked clips become a paste queue.</summary>
public sealed class PopupQueueTests : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly FakePasteFlow _paste = new();
    private readonly FakeQueueControl _queue = new();
    private readonly ClipLibrary _library;
    private readonly SettingsService _settings = new(new FakeSettingsStore());
    private readonly PopupViewModel _vm;

    public PopupQueueTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _time), new FakeClipStore());
        _vm = new PopupViewModel(_library, _paste, _queue, _settings, new ImmediateDispatcher(), _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _vm.Dispose();
        _settings.Dispose();
        _library.Dispose();
    }

    private async Task CopyAsync(params string[] texts)
    {
        foreach (var text in texts)
        {
            (await _library.AddAsync(TestClips.Text(text), Ct)).IsSuccess.ShouldBeTrue();
        }
    }

    private void Mark(params int[] positions)
    {
        foreach (var position in positions)
        {
            _vm.SelectedIndex = position;
            _vm.ToggleMarkCommand.Execute(null);
        }
    }

    private string[] Queued() => [.. _queue.Started.Single().Select(clip => ((TextContent)clip.Content).Text)];

    [Fact]
    public async Task Ctrl_q_queues_the_marked_clips_in_the_order_they_were_marked_and_closes_the_popup()
    {
        await CopyAsync("name", "street", "city");
        _vm.Open();
        Mark(2, 1, 0);

        await _vm.QueueCommand.ExecuteAsync(null);

        Queued().ShouldBe(["name", "street", "city"]);
        _vm.IsOpen.ShouldBeFalse();
        _paste.Pasted.ShouldBeEmpty();
        _paste.PastedContent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_marks_the_highlighted_clip_is_queued()
    {
        await CopyAsync("one", "two");
        _vm.Open();

        await _vm.QueueCommand.ExecuteAsync(null);

        Queued().ShouldBe(["two"]);
    }

    [Fact]
    public async Task A_queue_that_cannot_start_keeps_the_popup_open_and_says_why()
    {
        _queue.StartResult = Result.Failure(new ResultError(ErrorCode.HotkeyUnavailable, "Another program is using Ctrl+V, so the queue cannot start."));
        await CopyAsync("one", "two");
        _vm.Open();
        Mark(0, 1);

        await _vm.QueueCommand.ExecuteAsync(null);

        _vm.IsOpen.ShouldBeTrue();
        _vm.ErrorMessage.ShouldBe("Another program is using Ctrl+V, so the queue cannot start.");
        _vm.MarkedCount.ShouldBe(2);
    }

    [Fact]
    public async Task With_nothing_listed_nothing_is_queued()
    {
        _vm.Open();

        await _vm.QueueCommand.ExecuteAsync(null);

        _queue.Started.ShouldBeEmpty();
        _vm.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task The_footer_offers_the_queue_when_clips_are_marked()
    {
        await CopyAsync("a", "b");
        _vm.Open();
        Mark(0, 1);

        _vm.StatusText.ShouldBe("Enter paste 2 · Ctrl+S New line · Ctrl+Q queue · Ctrl+T table · Esc close");
    }

    [Fact]
    public void A_missing_queue_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new PopupViewModel(_library, _paste, null!, _settings, new ImmediateDispatcher(), _time));
    }
}
