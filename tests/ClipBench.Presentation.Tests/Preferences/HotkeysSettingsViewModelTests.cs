using ClipBench.Core.Hotkeys;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Preferences;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Preferences;

public sealed class HotkeysSettingsViewModelTests : IDisposable
{
    private readonly FakeSettingsStore _store = new();
    private readonly ImmediateDispatcher _dispatcher = new();
    private readonly FakeHotkeys _hotkeys = new();
    private readonly SettingsService _settings;
    private readonly HotkeyBindings _bindings;
    private readonly HotkeysSettingsViewModel _vm;

    public HotkeysSettingsViewModelTests()
    {
        _settings = new SettingsService(_store);
        _bindings = new HotkeyBindings(_hotkeys);
        _vm = new HotkeysSettingsViewModel(_settings, _bindings, _dispatcher);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _vm.Dispose();
        _bindings.Dispose();
        _settings.Dispose();
    }

    [Fact]
    public void It_shows_the_shortcuts_in_use_as_a_person_would_type_them()
    {
        _vm.PopupHotkey.ShouldBe("Win+Alt+V");
        _vm.CollectHotkey.ShouldBe("Win+Alt+C");
    }

    [Fact]
    public async Task A_new_shortcut_is_claimed_and_saved()
    {
        var applied = await _vm.SetAsync(HotkeyAction.OpenPopup, "ctrl+alt+k", Ct);

        applied.ShouldBeTrue();
        _vm.PopupHotkey.ShouldBe("Ctrl+Alt+K");
        _settings.Current.PopupHotkey.ToString().ShouldBe("Ctrl+Alt+K");
        _bindings.Current(HotkeyAction.OpenPopup)!.ToString().ShouldBe("Ctrl+Alt+K");
        _vm.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task The_collect_shortcut_is_changed_the_same_way()
    {
        await _vm.SetAsync(HotkeyAction.ToggleCollect, "Ctrl+Shift+F9", Ct);

        _vm.CollectHotkey.ShouldBe("Ctrl+Shift+F9");
        _settings.Current.CollectHotkey.ToString().ShouldBe("Ctrl+Shift+F9");
    }

    [Theory]
    [InlineData("")]
    [InlineData("K")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Nonsense")]
    public async Task Text_that_is_not_a_usable_shortcut_is_refused_with_the_reason_and_nothing_changes(string typed)
    {
        var applied = await _vm.SetAsync(HotkeyAction.OpenPopup, typed, Ct);

        applied.ShouldBeFalse();
        _vm.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
        _vm.PopupHotkey.ShouldBe("Win+Alt+V");
        _hotkeys.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_shortcut_another_program_holds_is_refused_and_the_old_one_keeps_working()
    {
        await _bindings.BindAsync(HotkeyAction.OpenPopup, Hotkey.Default, Ct);
        _hotkeys.Taken.Add(Hotkey.Parse("Ctrl+Alt+K").Value);

        var applied = await _vm.SetAsync(HotkeyAction.OpenPopup, "Ctrl+Alt+K", Ct);

        applied.ShouldBeFalse();
        _vm.ErrorMessage.ShouldBe("Another program already uses Ctrl+Alt+K. Choose a different shortcut.");
        _vm.PopupHotkey.ShouldBe("Win+Alt+V");
        _bindings.Current(HotkeyAction.OpenPopup).ShouldBe(Hotkey.Default);
    }

    [Fact]
    public async Task One_shortcut_cannot_do_both_things()
    {
        await _bindings.BindAsync(HotkeyAction.OpenPopup, Hotkey.Default, Ct);

        var applied = await _vm.SetAsync(HotkeyAction.ToggleCollect, "Win+Alt+V", Ct);

        applied.ShouldBeFalse();
        _vm.ErrorMessage.ShouldBe("That shortcut is already used for something else in ClipBench.");
        _vm.CollectHotkey.ShouldBe("Win+Alt+C");
    }

    [Fact]
    public async Task Resetting_puts_the_default_shortcut_back()
    {
        await _vm.SetAsync(HotkeyAction.OpenPopup, "Ctrl+Alt+K", Ct);

        await _vm.ResetPopupCommand.ExecuteAsync(null);

        _vm.PopupHotkey.ShouldBe("Win+Alt+V");
        _settings.Current.PopupHotkey.ShouldBe(Hotkey.Default);
    }

    [Fact]
    public async Task Resetting_the_collect_shortcut_puts_its_default_back()
    {
        await _vm.SetAsync(HotkeyAction.ToggleCollect, "Ctrl+Alt+K", Ct);

        await _vm.ResetCollectCommand.ExecuteAsync(null);

        _vm.CollectHotkey.ShouldBe("Win+Alt+C");
    }

    [Fact]
    public async Task A_shortcut_that_cannot_be_saved_still_works_for_this_run_and_the_reason_is_shown()
    {
        _store.Failure = new ResultError(ErrorCode.StorageFailure, "Your settings could not be saved.");

        var applied = await _vm.SetAsync(HotkeyAction.OpenPopup, "Ctrl+Alt+K", Ct);

        applied.ShouldBeFalse();
        _vm.ErrorMessage.ShouldBe("Your settings could not be saved.");
        _bindings.Current(HotkeyAction.OpenPopup)!.ToString().ShouldBe("Ctrl+Alt+K");
    }

    [Fact]
    public async Task A_new_try_clears_the_old_error()
    {
        await _vm.SetAsync(HotkeyAction.OpenPopup, "K", Ct);

        await _vm.SetAsync(HotkeyAction.OpenPopup, "Ctrl+Alt+K", Ct);

        _vm.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Missing_text_is_a_bug()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => _vm.SetAsync(HotkeyAction.OpenPopup, null!, Ct));
    }

    [Fact]
    public void Missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new HotkeysSettingsViewModel(null!, _bindings, _dispatcher));
        Should.Throw<ArgumentNullException>(() => new HotkeysSettingsViewModel(_settings, null!, _dispatcher));
    }

    private sealed class FakeHotkeys : IHotkeyService
    {
        private readonly Dictionary<int, Hotkey> _registered = [];
        private int _next = 1;

        public event EventHandler<HotkeyPressedEventArgs>? Pressed
        {
            add { }
            remove { }
        }

        public HashSet<Hotkey> Taken { get; } = [];

        public List<string> Calls { get; } = [];

        public Task<Result<HotkeyId>> RegisterAsync(Hotkey hotkey, CancellationToken cancellationToken)
        {
            if (Taken.Contains(hotkey))
            {
                return Task.FromResult(Result.Failure<HotkeyId>(new ResultError(ErrorCode.HotkeyUnavailable, "taken")));
            }

            var id = _next++;
            _registered[id] = hotkey;
            Calls.Add("+" + hotkey);
            return Task.FromResult(Result.Success(new HotkeyId(id)));
        }

        public Task<Result> UnregisterAsync(HotkeyId id, CancellationToken cancellationToken)
        {
            _ = _registered.Remove(id.Value);
            Calls.Add("-" + id.Value);
            return Task.FromResult(Result.Success());
        }

        public void Dispose()
        {
        }
    }
}
