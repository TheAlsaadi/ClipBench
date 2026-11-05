using ClipBench.Core.Capture;
using ClipBench.Core.History;
using ClipBench.Core.Hotkeys;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Core.Snippets;
using ClipBench.Presentation.Preferences;
using ClipBench.Presentation.Snippets;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Preferences;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly ImmediateDispatcher _dispatcher = new();
    private readonly SettingsService _settings = new(new FakeSettingsStore());
    private readonly HotkeyBindings _bindings = new(new NoHotkeys());
    private readonly SnippetCatalog _catalog = new(new FakeSnippetStore());
    private readonly SnippetEditorViewModel _editor;
    private readonly SettingsViewModel _vm;

    public SettingsViewModelTests()
    {
        _editor = new SnippetEditorViewModel(_catalog, new SnippetEngine(new SnippetTestClock()), _dispatcher);
        _vm = new SettingsViewModel(
            new GeneralSettingsViewModel(_settings, null, _dispatcher),
            new HotkeysSettingsViewModel(_settings, _bindings, _dispatcher),
            new HistorySettingsViewModel(_settings, _dispatcher),
            new PrivacySettingsViewModel(_settings, new Capture(), new Wipe(), _dispatcher),
            new PasteSettingsViewModel(_settings, _dispatcher),
            _editor,
            new AboutViewModel("1.0"));
    }

    public void Dispose()
    {
        _vm.Dispose();
        _editor.Dispose();
        _catalog.Dispose();
        _bindings.Dispose();
        _settings.Dispose();
    }

    [Fact]
    public void The_sections_come_in_the_order_the_window_shows_them()
    {
        SettingsViewModel.Sections.Select(item => item.Section).ShouldBe(
            [
                SettingsSection.General,
                SettingsSection.Hotkeys,
                SettingsSection.History,
                SettingsSection.Privacy,
                SettingsSection.Paste,
                SettingsSection.Snippets,
                SettingsSection.About,
            ]);
        SettingsViewModel.Sections.Select(item => item.Title).ShouldBe(["General", "Shortcuts", "History", "Privacy", "Paste", "Snippets", "About"]);
    }

    [Fact]
    public void It_opens_on_the_general_section()
    {
        _vm.SelectedSection.ShouldBe(SettingsSection.General);
        _vm.IsShown(SettingsSection.General).ShouldBeTrue();
        _vm.IsShown(SettingsSection.About).ShouldBeFalse();
    }

    [Fact]
    public void Showing_a_section_selects_it_and_tells_the_bindings()
    {
        var raised = new List<string?>();
        _vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _vm.Show(SettingsSection.Privacy);

        _vm.SelectedSection.ShouldBe(SettingsSection.Privacy);
        _vm.IsShown(SettingsSection.Privacy).ShouldBeTrue();
        raised.ShouldContain(nameof(SettingsViewModel.SelectedSection));
    }

    [Fact]
    public void An_unknown_section_is_a_bug()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _vm.Show((SettingsSection)99));
    }

    [Fact]
    public void Every_section_view_model_is_reachable_for_the_window()
    {
        _vm.General.ShouldNotBeNull();
        _vm.Hotkeys.ShouldNotBeNull();
        _vm.History.ShouldNotBeNull();
        _vm.Privacy.ShouldNotBeNull();
        _vm.Paste.ShouldNotBeNull();
        _vm.Snippets.ShouldBeSameAs(_editor);
        _vm.About.Version.ShouldBe("1.0");
    }

    [Fact]
    public void Missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new SettingsViewModel(null!, _vm.Hotkeys, _vm.History, _vm.Privacy, _vm.Paste, _editor, _vm.About));
        Should.Throw<ArgumentNullException>(() => new SettingsViewModel(_vm.General, _vm.Hotkeys, _vm.History, _vm.Privacy, _vm.Paste, _editor, null!));
    }

    private sealed class NoHotkeys : IHotkeyService
    {
        public event EventHandler<HotkeyPressedEventArgs>? Pressed
        {
            add { }
            remove { }
        }

        public Task<Result<HotkeyId>> RegisterAsync(Hotkey hotkey, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new HotkeyId(1)));

        public Task<Result> UnregisterAsync(HotkeyId id, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public void Dispose()
        {
        }
    }

    private sealed class Capture : ICaptureControl
    {
        public bool IsPaused { get; private set; }

        public event EventHandler? PausedChanged
        {
            add { }
            remove { }
        }

        public void SetPaused(bool paused) => IsPaused = paused;
    }

    private sealed class Wipe : IWipeAll
    {
        public Task<Result> WipeAllAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }
}
