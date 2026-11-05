using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Preferences;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Preferences;

public sealed class HistoryAndPasteSettingsTests : IDisposable
{
    private readonly FakeSettingsStore _store = new();
    private readonly ImmediateDispatcher _dispatcher = new();
    private readonly SettingsService _settings;
    private readonly HistorySettingsViewModel _history;
    private readonly PasteSettingsViewModel _paste;

    public HistoryAndPasteSettingsTests()
    {
        _settings = new SettingsService(_store);
        _history = new HistorySettingsViewModel(_settings, _dispatcher);
        _paste = new PasteSettingsViewModel(_settings, _dispatcher);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _history.Dispose();
        _paste.Dispose();
        _settings.Dispose();
    }

    // --- history ---

    [Fact]
    public void The_history_section_starts_with_the_default_limits_and_publishes_their_ranges()
    {
        _history.MaxCount.ShouldBe(1000);
        _history.MaxAgeDays.ShouldBe(30);
        _history.MaxMegabytes.ShouldBe(256);
        HistorySettingsViewModel.MaxCountRange.ShouldBe(AppSettings.HistoryMaxCountRange);
        HistorySettingsViewModel.MaxAgeDaysRange.ShouldBe(AppSettings.HistoryMaxAgeDaysRange);
        HistorySettingsViewModel.MaxMegabytesRange.ShouldBe(AppSettings.HistoryMaxMegabytesRange);
    }

    [Fact]
    public async Task Each_limit_is_saved_when_it_is_changed()
    {
        _history.MaxCount = 250;
        _history.MaxAgeDays = 7;
        _history.MaxMegabytes = 64;
        await Task.Yield();

        _settings.Current.HistoryMaxCount.ShouldBe(250);
        _settings.Current.HistoryMaxAgeDays.ShouldBe(7);
        _settings.Current.HistoryMaxMegabytes.ShouldBe(64);
        _store.Saved.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_number_outside_its_range_is_moved_to_the_nearest_end_and_the_box_shows_it()
    {
        var raised = new List<string?>();
        _history.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _history.MaxCount = 5;
        await Task.Yield();
        _history.MaxAgeDays = 100_000;
        await Task.Yield();

        _history.MaxCount.ShouldBe(10);
        _history.MaxAgeDays.ShouldBe(365);
        raised.ShouldContain(string.Empty);
    }

    [Fact]
    public async Task A_limit_that_cannot_be_saved_shows_the_reason_but_still_applies()
    {
        _store.Failure = new ResultError(ErrorCode.StorageFailure, "Your settings could not be saved.");

        _history.MaxCount = 500;
        await Task.Yield();

        _history.MaxCount.ShouldBe(500);
        _history.ErrorMessage.ShouldBe("Your settings could not be saved.");
    }

    // --- paste ---

    [Fact]
    public void The_paste_section_starts_with_the_defaults()
    {
        _paste.RestorePreviousClipboard.ShouldBeTrue();
        _paste.KeepCopiesWhileCollecting.ShouldBeFalse();
        _paste.SelectedSeparator.ShouldBe(SeparatorChoice.NewLine);
        _paste.IsCustomSelected.ShouldBeFalse();
        _paste.CustomSeparator.ShouldBeEmpty();
        PasteSettingsViewModel.SeparatorOptions.ShouldBe(SeparatorOption.Presets);
    }

    [Fact]
    public async Task The_two_switches_are_saved()
    {
        _paste.RestorePreviousClipboard = false;
        _paste.KeepCopiesWhileCollecting = true;
        await Task.Yield();

        _settings.Current.RestorePreviousClipboard.ShouldBeFalse();
        _settings.Current.KeepCopiesWhileCollecting.ShouldBeTrue();
    }

    [Fact]
    public async Task Choosing_a_preset_separator_saves_it()
    {
        _paste.SelectedSeparator = SeparatorChoice.Semicolon;
        await Task.Yield();

        _settings.Current.Separator.ShouldBe(SeparatorChoice.Semicolon);
        _paste.SelectedSeparator.ShouldBe(SeparatorChoice.Semicolon);
    }

    [Fact]
    public async Task A_custom_separator_that_can_be_read_is_saved_and_selected()
    {
        await _paste.ApplyCustomSeparatorCommand.ExecuteAsync(@" | \t");

        _settings.Current.Separator.ShouldBe(SeparatorChoice.Custom);
        _settings.Current.CustomSeparator.ShouldBe(@" | \t");
        _paste.IsCustomSelected.ShouldBeTrue();
        _paste.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task A_custom_separator_that_cannot_be_read_is_refused_with_the_reason()
    {
        await _paste.ApplyCustomSeparatorCommand.ExecuteAsync(@"\q");

        _settings.Current.Separator.ShouldBe(SeparatorChoice.NewLine);
        _paste.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_empty_custom_separator_means_nothing_between_clips()
    {
        await _paste.ApplyCustomSeparatorCommand.ExecuteAsync(string.Empty);

        _settings.Current.Separator.ShouldBe(SeparatorChoice.None);
    }

    [Fact]
    public void Missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new HistorySettingsViewModel(null!, _dispatcher));
        Should.Throw<ArgumentNullException>(() => new PasteSettingsViewModel(_settings, null!));
    }

    // --- about ---

    [Fact]
    public void The_about_section_names_the_version_the_license_the_promise_and_the_repository()
    {
        var about = new AboutViewModel("1.2.3");

        about.Version.ShouldBe("1.2.3");
        AboutViewModel.License.ShouldContain("MIT");
        AboutViewModel.NoNetworkPromise.ShouldContain("no network");
        AboutViewModel.RepositoryUrl.ShouldBe("https://github.com/TheAlsaadi/ClipBench");
        AboutViewModel.Title.ShouldBe("ClipBench");
    }

    [Fact]
    public void Opening_the_repository_asks_the_app_to_open_the_address_and_does_nothing_itself()
    {
        var about = new AboutViewModel("1.0");
        string? asked = null;
        about.OpenRepositoryRequested += (_, e) => asked = e.Url;

        about.OpenRepositoryCommand.Execute(null);

        asked.ShouldBe("https://github.com/TheAlsaadi/ClipBench");
    }

    [Fact]
    public void A_missing_version_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new AboutViewModel(null!));
    }
}
