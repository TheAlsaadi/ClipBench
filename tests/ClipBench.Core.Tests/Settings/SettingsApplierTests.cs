using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.History;
using ClipBench.Core.Privacy;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Settings;

public sealed class SettingsApplierTests : IDisposable
{
    private readonly FakeSettingsStore _settingsStore = new();
    private readonly FakeClipStore _clipStore = new();
    private readonly FakeActivityLog _log = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly SettingsService _settings;
    private readonly ClipLibrary _library;
    private readonly CaptureService _capture;
    private readonly SettingsApplier _applier;

    public SettingsApplierTests()
    {
        _settings = new SettingsService(_settingsStore);
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _clock), _clipStore);
        _capture = new CaptureService(new FakeClipboardListener(), new FakeClipboardReader(), _library, _log, _clock);
        _applier = new SettingsApplier(_settings, _library, _capture, _log);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _applier.Dispose();
        _capture.Dispose();
        _library.Dispose();
        _settings.Dispose();
    }

    private async Task AddAsync(params string[] texts)
    {
        foreach (var text in texts)
        {
            await _library.AddAsync(TestClips.Text(text, _clock.GetUtcNow()), Ct);
        }
    }

    private string[] Texts() => [.. _library.Clips.Select(clip => ((TextContent)clip.Content).Text)];

    [Fact]
    public async Task Lowering_the_history_size_applies_to_the_next_copies()
    {
        await _settings.UpdateAsync(settings => settings with { HistoryMaxCount = 10 }, Ct);
        await _applier.WhenIdleAsync();

        await AddAsync([.. Enumerable.Range(0, 12).Select(i => $"clip {i}")]);

        _library.Clips.Length.ShouldBe(10);
    }

    [Fact]
    public async Task A_shorter_age_removes_the_clips_that_are_too_old_at_once()
    {
        await AddAsync("old");
        _clock.Advance(TimeSpan.FromDays(3));
        await AddAsync("new");

        await _settings.UpdateAsync(settings => settings with { HistoryMaxAgeDays = 2 }, Ct);
        await _applier.WhenIdleAsync();

        Texts().ShouldBe(["new"]);
        _clipStore.Deleted.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Settings_that_are_loaded_are_applied_too()
    {
        await AddAsync("old");
        _clock.Advance(TimeSpan.FromDays(20));
        await AddAsync("new");
        Texts().ShouldBe(["new", "old"]);
        _settingsStore.Load = new SettingsLoad(AppSettings.Default with { HistoryMaxAgeDays = 10 }, SettingsLoadStatus.Loaded);

        await _settings.LoadAsync(Ct);
        await _applier.WhenIdleAsync();

        Texts().ShouldBe(["new"]);
    }

    [Fact]
    public async Task A_change_to_something_else_does_not_touch_the_history()
    {
        await AddAsync("one");

        await _settings.UpdateAsync(settings => settings with { RestorePreviousClipboard = false }, Ct);
        await _applier.WhenIdleAsync();

        _clipStore.Deleted.ShouldBeEmpty();
        Texts().ShouldBe(["one"]);
    }

    [Fact]
    public async Task A_store_that_cannot_delete_is_logged_and_does_not_stop_the_app()
    {
        await AddAsync("old");
        _clock.Advance(TimeSpan.FromDays(3));
        _clipStore.Failure = new ResultError(ErrorCode.StorageFailure, "the disk said no");

        await _settings.UpdateAsync(settings => settings with { HistoryMaxAgeDays = 2 }, Ct);
        await _applier.WhenIdleAsync();

        _log.Entries.ShouldContain(entry => entry.Activity == ActivityEvent.StoreFailed && entry.Level == ActivityLevel.Warning);
    }

    // --- excluded programs ---

    [Fact]
    public void From_the_start_the_default_exclusions_are_in_force()
    {
        _capture.Filter.ExcludedProcesses.ShouldBe(ExclusionList.Default.Names);
    }

    [Fact]
    public async Task Changing_the_excluded_programs_changes_what_is_captured_at_once()
    {
        var list = ExclusionList.Empty.Add("notepad").Value;

        await _settings.UpdateAsync(settings => settings with { ExcludedPrograms = list }, Ct);

        _capture.Filter.ExcludedProcesses.ShouldBe(["notepad"]);
    }

    [Fact]
    public async Task Loaded_exclusions_are_applied()
    {
        _settingsStore.Load = new SettingsLoad(AppSettings.Default with { ExcludedPrograms = ExclusionList.Empty }, SettingsLoadStatus.Loaded);

        await _settings.LoadAsync(Ct);

        _capture.Filter.ExcludedProcesses.ShouldBeEmpty();
    }

    [Fact]
    public async Task Applying_exclusions_leaves_incognito_and_the_size_limits_alone()
    {
        var limits = new CaptureLimits { MaxTextBytes = 1234, MaxImageBytes = 5678 };
        _capture.UpdateFilter(filter => filter with { Limits = limits });
        _capture.SetPaused(true);

        await _settings.UpdateAsync(settings => settings with { ExcludedPrograms = ExclusionList.Empty }, Ct);

        _capture.IsPaused.ShouldBeTrue();
        _capture.Filter.Limits.ShouldBe(limits);
    }

    [Fact]
    public async Task After_it_is_disposed_changes_are_no_longer_applied()
    {
        await AddAsync("one");
        _clock.Advance(TimeSpan.FromDays(5));
        _applier.Dispose();

        await _settings.UpdateAsync(settings => settings with { HistoryMaxAgeDays = 1 }, Ct);
        await _applier.WhenIdleAsync();

        Texts().ShouldBe(["one"]);
    }

    [Fact]
    public void Missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new SettingsApplier(null!, _library, _capture, _log));
        Should.Throw<ArgumentNullException>(() => new SettingsApplier(_settings, null!, _capture, _log));
        Should.Throw<ArgumentNullException>(() => new SettingsApplier(_settings, _library, null!, _log));
        Should.Throw<ArgumentNullException>(() => new SettingsApplier(_settings, _library, _capture, null!));
    }
}
