using ClipBench.Core.History;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Collect;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Popup;

public sealed class PopupSeparatorTests : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly FakeSettingsStore _settingsStore = new();
    private readonly ClipLibrary _library;
    private readonly SettingsService _settings;
    private readonly PopupViewModel _vm;

    public PopupSeparatorTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _time), new FakeClipStore());
        _settings = new SettingsService(_settingsStore);
        _vm = new PopupViewModel(_library, new FakePasteFlow(), new FakeQueueControl(), new CollectToggle(new FakeCollectControl(), new FakeNotifier()), TestPickers.New(), _settings, new ImmediateDispatcher(), _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _vm.Dispose();
        _settings.Dispose();
        _library.Dispose();
    }

    private async Task LoadAsync(AppSettings settings)
    {
        _settingsStore.Load = new SettingsLoad(settings, SettingsLoadStatus.Loaded);
        await _settings.LoadAsync(Ct);
    }

    [Fact]
    public void The_separator_shown_is_the_one_in_the_settings()
    {
        _vm.SeparatorLabel.ShouldBe("New line");
    }

    [Fact]
    public async Task Ctrl_S_moves_through_the_presets_in_order_and_wraps()
    {
        var seen = new List<string>();
        for (var step = 0; step < 9; step++)
        {
            await _vm.NextSeparatorCommand.ExecuteAsync(null);
            seen.Add(_vm.SeparatorLabel);
        }

        seen.ShouldBe(["Blank line", "Comma and space", "Comma", "Semicolon", "Tab", "Space", "Nothing", "New line", "Blank line"]);
    }

    [Fact]
    public async Task A_saved_custom_separator_takes_its_turn_after_the_presets()
    {
        await LoadAsync(AppSettings.Default with { Separator = SeparatorChoice.None, CustomSeparator = @" | " });

        await _vm.NextSeparatorCommand.ExecuteAsync(null);
        _vm.SeparatorLabel.ShouldBe("Custom \" | \"");
        await _vm.NextSeparatorCommand.ExecuteAsync(null);
        _vm.SeparatorLabel.ShouldBe("New line");
    }

    [Fact]
    public async Task Each_choice_is_saved_so_it_is_remembered_next_time()
    {
        await _vm.NextSeparatorCommand.ExecuteAsync(null);

        _settings.Current.Separator.ShouldBe(SeparatorChoice.BlankLine);
        _settingsStore.Saved.ShouldBe([AppSettings.Default with { Separator = SeparatorChoice.BlankLine }]);
    }

    [Fact]
    public async Task A_separator_can_be_chosen_from_the_list()
    {
        PopupViewModel.SeparatorOptions.Select(option => option.Choice).ShouldBe(
        [
            SeparatorChoice.NewLine, SeparatorChoice.BlankLine, SeparatorChoice.CommaSpace, SeparatorChoice.Comma,
            SeparatorChoice.Semicolon, SeparatorChoice.Tab, SeparatorChoice.Space, SeparatorChoice.None,
        ]);

        await _vm.ChooseSeparatorCommand.ExecuteAsync(SeparatorChoice.Tab);

        _settings.Current.Separator.ShouldBe(SeparatorChoice.Tab);
        _vm.SeparatorLabel.ShouldBe("Tab");
    }

    [Fact]
    public async Task A_typed_separator_becomes_the_custom_one_and_is_used()
    {
        await _vm.SetCustomSeparatorCommand.ExecuteAsync(@"\r\n--\r\n");

        _settings.Current.Separator.ShouldBe(SeparatorChoice.Custom);
        _settings.Current.CustomSeparator.ShouldBe(@"\r\n--\r\n");
        _vm.SeparatorLabel.ShouldBe(@"Custom ""\r\n--\r\n""");
        _vm.ErrorMessage.ShouldBeNull();
    }

    [Theory]
    [InlineData(@"\q", "Unknown escape sequence. Use \\n, \\r, \\t or \\\\.")]
    [InlineData("ends with \\", "A separator cannot end with a lone backslash. Type \\\\ for one backslash.")]
    public async Task A_typed_separator_that_cannot_be_read_says_why_and_changes_nothing(string typed, string message)
    {
        await _vm.SetCustomSeparatorCommand.ExecuteAsync(typed);

        _vm.ErrorMessage.ShouldBe(message);
        _settings.Current.ShouldBe(AppSettings.Default);
        _settingsStore.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_typed_separator_that_is_too_long_says_why_and_changes_nothing()
    {
        await _vm.SetCustomSeparatorCommand.ExecuteAsync(new string('-', AppSettings.MaxTypedSeparatorLength + 1));

        _vm.ErrorMessage.ShouldBe("A separator can have at most 64 characters.");
        _settings.Current.ShouldBe(AppSettings.Default);
    }

    [Fact]
    public async Task An_empty_typed_separator_means_nothing_between_the_clips()
    {
        await _vm.SetCustomSeparatorCommand.ExecuteAsync(string.Empty);

        _settings.Current.Separator.ShouldBe(SeparatorChoice.None);
        _vm.SeparatorLabel.ShouldBe("Nothing");
    }

    [Fact]
    public async Task A_choice_that_cannot_be_saved_still_applies_and_the_popup_says_it_was_not_saved()
    {
        _settingsStore.Failure = new ResultError(ErrorCode.StorageFailure, "the disk said no");

        await _vm.NextSeparatorCommand.ExecuteAsync(null);

        _vm.SeparatorLabel.ShouldBe("Blank line");
        _vm.ErrorMessage.ShouldBe("That change could not be saved.");
    }

    [Fact]
    public async Task A_change_made_elsewhere_is_shown()
    {
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await _settings.UpdateAsync(settings => settings with { Separator = SeparatorChoice.Semicolon }, Ct);

        changed.ShouldContain(nameof(PopupViewModel.SeparatorLabel));
        _vm.SeparatorLabel.ShouldBe("Semicolon");
    }

    [Fact]
    public async Task After_disposal_settings_changes_are_no_longer_followed()
    {
        var changed = 0;
        _vm.PropertyChanged += (_, _) => changed++;
        _vm.Dispose();

        await _settings.UpdateAsync(settings => settings with { Separator = SeparatorChoice.Tab }, Ct);

        changed.ShouldBe(0);
    }

    [Fact]
    public void Missing_settings_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new PopupViewModel(_library, new FakePasteFlow(), new FakeQueueControl(), new CollectToggle(new FakeCollectControl(), new FakeNotifier()), TestPickers.New(), null!, new ImmediateDispatcher(), _time));
    }
}
