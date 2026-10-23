using ClipBench.Core.History;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Collect;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Popup;

/// <summary>Ctrl+L in the popup starts collecting, and finishes it when a session runs.</summary>
public sealed class PopupCollectTests : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly FakeCollectControl _collect = new();
    private readonly FakeNotifier _notifier = new();
    private readonly ClipLibrary _library;
    private readonly SettingsService _settings = new(new FakeSettingsStore());
    private readonly PopupViewModel _vm;

    public PopupCollectTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _time), new FakeClipStore());
        _vm = new PopupViewModel(
            _library,
            new FakePasteFlow(),
            new FakeQueueControl(),
            new CollectToggle(_collect, _notifier),
            TestPickers.New(),
            TestSidebars.New(_library),
            TestSnippets.New(),
            _settings,
            new ImmediateDispatcher(),
            _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _vm.Dispose();
        _settings.Dispose();
        _library.Dispose();
    }

    [Fact]
    public async Task Starting_from_the_popup_closes_it_so_the_user_can_go_and_copy()
    {
        _vm.Open();

        await _vm.ToggleCollectCommand.ExecuteAsync(null);

        _collect.IsActive.ShouldBeTrue();
        _vm.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task Finishing_from_the_popup_keeps_it_open_so_the_collected_clip_can_be_pasted()
    {
        _collect.Start();
        _collect.Collected();
        _vm.Open();

        await _vm.ToggleCollectCommand.ExecuteAsync(null);

        _collect.Finishes.ShouldBe(1);
        _vm.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task While_collecting_the_footer_says_how_many_copies_and_how_to_finish()
    {
        await _library.AddAsync(TestClips.Text("one"), Ct);
        _collect.Start();
        _collect.Collected();
        _collect.Collected();

        _vm.Open();

        _vm.StatusText.ShouldBe("Collecting 2 · Ctrl+L finish · Enter paste · Space select · Esc close");
        _vm.IsCollecting.ShouldBeTrue();
    }

    [Fact]
    public async Task The_footer_follows_the_session()
    {
        await _library.AddAsync(TestClips.Text("one"), Ct);
        _vm.Open();
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _collect.Start();

        changed.ShouldContain(nameof(PopupViewModel.StatusText));
        changed.ShouldContain(nameof(PopupViewModel.IsCollecting));
    }

    [Fact]
    public void The_collect_button_offers_to_start_and_then_to_finish_with_the_count()
    {
        _vm.Open();
        _vm.CollectLabel.ShouldBe("Collect");
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _collect.Start();
        _collect.Collected();

        _vm.CollectLabel.ShouldBe("Finish (1)");
        changed.ShouldContain(nameof(PopupViewModel.CollectLabel));
    }

    [Fact]
    public void After_disposal_the_session_is_no_longer_followed()
    {
        _vm.Dispose();

        _collect.SubscriberCount.ShouldBe(0);
    }

    [Fact]
    public void A_missing_collect_toggle_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new PopupViewModel(
            _library, new FakePasteFlow(), new FakeQueueControl(), null!, TestPickers.New(), TestSidebars.New(_library), TestSnippets.New(), _settings, new ImmediateDispatcher(), _time));
    }
}
