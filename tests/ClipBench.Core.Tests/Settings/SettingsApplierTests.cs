using ClipBench.Core.Clips;
using ClipBench.Core.Diagnostics;
using ClipBench.Core.History;
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
    private readonly SettingsApplier _applier;

    public SettingsApplierTests()
    {
        _settings = new SettingsService(_settingsStore);
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _clock), _clipStore);
        _applier = new SettingsApplier(_settings, _library, _log);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _applier.Dispose();
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
        Should.Throw<ArgumentNullException>(() => new SettingsApplier(null!, _library, _log));
        Should.Throw<ArgumentNullException>(() => new SettingsApplier(_settings, null!, _log));
        Should.Throw<ArgumentNullException>(() => new SettingsApplier(_settings, _library, null!));
    }
}
