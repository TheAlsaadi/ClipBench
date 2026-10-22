using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Settings;

public class SettingsServiceTests
{
    private static readonly ResultError _storeError = new(ErrorCode.StorageFailure, "the disk said no");

    private readonly FakeSettingsStore _store = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Before_loading_the_settings_are_the_defaults()
    {
        using var service = new SettingsService(_store);

        service.Current.ShouldBe(AppSettings.Default);
    }

    [Fact]
    public async Task Loading_uses_the_stored_settings_and_reports_how_they_were_found()
    {
        var stored = AppSettings.Default with { Separator = SeparatorChoice.Tab, RestorePreviousClipboard = false };
        _store.Load = new SettingsLoad(stored, SettingsLoadStatus.Loaded);
        using var service = new SettingsService(_store);

        var status = await service.LoadAsync(Ct);

        status.ShouldBe(SettingsLoadStatus.Loaded);
        service.Current.ShouldBe(stored);
    }

    [Fact]
    public async Task Loading_normalizes_what_the_store_returned()
    {
        _store.Load = new SettingsLoad(AppSettings.Default with { Separator = SeparatorChoice.Custom, CustomSeparator = @"\q" }, SettingsLoadStatus.Loaded);
        using var service = new SettingsService(_store);

        await service.LoadAsync(Ct);

        service.Current.Separator.ShouldBe(SeparatorChoice.NewLine);
        service.Current.CustomSeparator.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task Loading_raises_changed_once()
    {
        using var service = new SettingsService(_store);
        var raised = 0;
        service.Changed += (_, _) => raised++;

        await service.LoadAsync(Ct);

        raised.ShouldBe(1);
    }

    [Fact]
    public async Task An_update_changes_the_settings_saves_them_and_raises_changed()
    {
        using var service = new SettingsService(_store);
        var raised = 0;
        service.Changed += (_, _) => raised++;

        var result = await service.UpdateAsync(settings => settings with { Separator = SeparatorChoice.Comma }, Ct);

        result.IsSuccess.ShouldBeTrue();
        service.Current.Separator.ShouldBe(SeparatorChoice.Comma);
        _store.Saved.ShouldBe([AppSettings.Default with { Separator = SeparatorChoice.Comma }]);
        raised.ShouldBe(1);
    }

    [Fact]
    public async Task An_update_that_changes_nothing_is_not_saved_or_announced()
    {
        using var service = new SettingsService(_store);
        var raised = 0;
        service.Changed += (_, _) => raised++;

        var result = await service.UpdateAsync(settings => settings with { }, Ct);

        result.IsSuccess.ShouldBeTrue();
        _store.Saved.ShouldBeEmpty();
        raised.ShouldBe(0);
    }

    [Fact]
    public async Task An_update_is_normalized_before_it_is_kept()
    {
        using var service = new SettingsService(_store);

        await service.UpdateAsync(settings => settings with { Separator = SeparatorChoice.Custom, CustomSeparator = @"\q" }, Ct);

        service.Current.ShouldBe(AppSettings.Default);
        _store.Saved.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_update_the_store_cannot_save_still_applies_and_the_failure_is_returned()
    {
        _store.Failure = _storeError;
        using var service = new SettingsService(_store);

        var result = await service.UpdateAsync(settings => settings with { RestorePreviousClipboard = false }, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_storeError);
        service.Current.RestorePreviousClipboard.ShouldBeFalse();
    }

    [Fact]
    public async Task Updates_are_saved_one_at_a_time_in_the_order_they_were_made()
    {
        var release = new TaskCompletionSource();
        _store.DuringSave = () => release.Task;
        using var service = new SettingsService(_store);

        var first = service.UpdateAsync(settings => settings with { Separator = SeparatorChoice.Comma }, Ct);
        var second = service.UpdateAsync(settings => settings with { RestorePreviousClipboard = false }, Ct);
        release.SetResult();
        await Task.WhenAll(first, second);

        _store.MaxConcurrentSaves.ShouldBe(1);
        _store.Saved.ShouldBe(
        [
            AppSettings.Default with { Separator = SeparatorChoice.Comma },
            AppSettings.Default with { Separator = SeparatorChoice.Comma, RestorePreviousClipboard = false },
        ]);
    }

    [Fact]
    public async Task A_subscriber_that_throws_does_not_stop_the_others()
    {
        using var service = new SettingsService(_store);
        var told = false;
        service.Changed += (_, _) => throw new InvalidOperationException("subscriber bug");
        service.Changed += (_, _) => told = true;

        var result = await service.UpdateAsync(settings => settings with { KeepCopiesWhileCollecting = true }, Ct);

        result.IsSuccess.ShouldBeTrue();
        told.ShouldBeTrue();
    }

    [Fact]
    public async Task A_missing_change_is_a_bug()
    {
        using var service = new SettingsService(_store);

        await Should.ThrowAsync<ArgumentNullException>(() => service.UpdateAsync(null!, Ct));
    }

    [Fact]
    public void A_missing_store_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new SettingsService(null!));
    }
}
