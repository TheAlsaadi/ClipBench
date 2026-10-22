using ClipBench.Core.Results;
using ClipBench.Presentation.Collect;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Collect;

public class CollectToggleTests
{
    private readonly FakeCollectControl _collect = new();
    private readonly FakeNotifier _notifier = new();
    private readonly CollectToggle _toggle;

    public CollectToggleTests()
    {
        _toggle = new CollectToggle(_collect, _notifier);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Toggling_when_nothing_is_collected_starts_a_session_and_says_how_it_works()
    {
        await _toggle.ToggleAsync(Ct);

        _collect.IsActive.ShouldBeTrue();
        _notifier.Shown.ShouldBe([("ClipBench", "Collecting. Every copy you make is added to one clip until you finish.")]);
    }

    [Fact]
    public async Task Toggling_while_collecting_finishes_and_says_how_many_copies_went_in()
    {
        await _toggle.ToggleAsync(Ct);
        _collect.Collected();
        _collect.Collected();
        _collect.Collected();

        await _toggle.ToggleAsync(Ct);

        _collect.Finishes.ShouldBe(1);
        _notifier.Shown[^1].Message.ShouldBe("Collected 3 copies into one clip. It is on your clipboard and at the top of your history.");
    }

    [Fact]
    public async Task One_copy_is_counted_in_the_singular()
    {
        await _toggle.ToggleAsync(Ct);
        _collect.Collected();

        await _toggle.ToggleAsync(Ct);

        _notifier.Shown[^1].Message.ShouldBe("Collected 1 copy. It is on your clipboard and at the top of your history.");
    }

    [Fact]
    public async Task Finishing_with_nothing_collected_says_so()
    {
        await _toggle.ToggleAsync(Ct);

        await _toggle.ToggleAsync(Ct);

        _notifier.Shown[^1].Message.ShouldBe("Collecting stopped. Nothing was collected.");
    }

    [Fact]
    public async Task A_finish_that_failed_adds_no_notice_of_its_own_because_the_session_already_explained()
    {
        _collect.FinishResult = Result.Failure(new ResultError(ErrorCode.ClipboardBusy, "busy"));
        await _toggle.ToggleAsync(Ct);
        _collect.Collected();

        await _toggle.ToggleAsync(Ct);

        _notifier.Shown.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Cancelling_throws_the_session_away_quietly()
    {
        await _toggle.ToggleAsync(Ct);

        _toggle.Cancel();

        _collect.Cancels.ShouldBe(1);
        _notifier.Shown.Count.ShouldBe(1);
    }

    [Fact]
    public void The_default_collect_shortcut_is_win_alt_c()
    {
        CollectToggle.DefaultShortcut.ToString().ShouldBe("Win+Alt+C");
    }

    [Fact]
    public void The_session_is_there_to_read_its_state()
    {
        _toggle.Collect.ShouldBeSameAs(_collect);
    }

    [Fact]
    public void Missing_services_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new CollectToggle(null!, _notifier));
        Should.Throw<ArgumentNullException>(() => new CollectToggle(_collect, null!));
    }
}
