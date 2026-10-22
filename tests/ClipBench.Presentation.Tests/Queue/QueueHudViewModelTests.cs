using ClipBench.Core.Pasting;
using ClipBench.Presentation.Queue;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Queue;

public sealed class QueueHudViewModelTests : IDisposable
{
    private readonly FakeQueueControl _queue = new();
    private readonly ImmediateDispatcher _dispatcher = new();
    private readonly QueueHudViewModel _hud;

    public QueueHudViewModelTests()
    {
        _hud = new QueueHudViewModel(_queue, _dispatcher);
    }

    public void Dispose() => _hud.Dispose();

    [Fact]
    public void Without_a_queue_nothing_is_shown()
    {
        _hud.IsVisible.ShouldBeFalse();
    }

    [Fact]
    public void A_started_queue_shows_which_clip_comes_next_and_how_to_stop()
    {
        _queue.Report(QueueState.Armed, 0, 5);

        _hud.IsVisible.ShouldBeTrue();
        _hud.Text.ShouldBe("Queue 1/5 · Esc to stop");
        _hud.AutomationText.ShouldBe("Paste queue, next clip 1 of 5. Press Esc to stop.");
    }

    [Fact]
    public void Each_paste_moves_the_count_on()
    {
        _queue.Report(QueueState.Armed, 0, 5);
        var changed = new List<string?>();
        _hud.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _queue.Report(QueueState.Armed, 1, 5);

        _hud.Text.ShouldBe("Queue 2/5 · Esc to stop");
        changed.ShouldContain(nameof(QueueHudViewModel.Text));
        changed.ShouldContain(nameof(QueueHudViewModel.AutomationText));
    }

    [Fact]
    public void While_the_last_clip_is_being_pasted_the_count_does_not_pass_the_end()
    {
        _queue.Report(QueueState.Pasting, 5, 5);

        _hud.Text.ShouldBe("Queue 5/5 · Esc to stop");
    }

    [Theory]
    [InlineData(QueueState.Exhausted)]
    [InlineData(QueueState.Cancelled)]
    [InlineData(QueueState.Idle)]
    public void A_queue_that_ended_is_hidden(QueueState state)
    {
        _queue.Report(QueueState.Armed, 0, 2);

        _queue.Report(state, 2, 2);

        _hud.IsVisible.ShouldBeFalse();
    }

    [Fact]
    public void Updates_reach_the_view_through_the_ui_thread()
    {
        var before = _dispatcher.Posts;

        _queue.Report(QueueState.Armed, 0, 1);

        _dispatcher.Posts.ShouldBe(before + 1);
    }

    [Fact]
    public void A_queue_already_running_when_it_is_created_is_shown()
    {
        _queue.Report(QueueState.Armed, 2, 3);

        using var hud = new QueueHudViewModel(_queue, _dispatcher);

        hud.IsVisible.ShouldBeTrue();
        hud.Text.ShouldBe("Queue 3/3 · Esc to stop");
    }

    [Fact]
    public void After_disposal_the_queue_is_no_longer_followed()
    {
        _hud.Dispose();

        _queue.SubscriberCount.ShouldBe(0);
    }

    [Fact]
    public void Missing_services_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new QueueHudViewModel(null!, _dispatcher));
        Should.Throw<ArgumentNullException>(() => new QueueHudViewModel(_queue, null!));
    }
}
