using ClipBench.Core.Tray;
using ClipBench.Platform.Windows.IntegrationTests.Support;
using ClipBench.Platform.Windows.Tray;

namespace ClipBench.Platform.Windows.IntegrationTests.Tray;

/// <summary>The icon can show a second picture, so the user can see at a glance that capture is off.</summary>
public sealed class TrayIconAlternateTests : IDisposable
{
    private const nint Handle = 0x4242;
    private const string IconPath = @"C:\app\Assets\ClipBench.ico";
    private const string AlternatePath = @"C:\app\Assets\ClipBench-incognito.ico";
    private const nint Normal = 0x77;
    private const nint Alternate = 0x88;

    private readonly FakeWindow _window = new();
    private readonly FakeTrayApi _api;
    private readonly TrayIcon _icon;

    public TrayIconAlternateTests()
    {
        _api = new FakeTrayApi(_window) { AlternatePath = AlternatePath };
        _icon = new TrayIcon(_window, _window, Handle, _api, new FakeTrayMenu(_window), IconPath, alternateIconPath: AlternatePath);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _icon.Dispose();

    private static TrayModel Model(bool alternate) => new("ClipBench", [new TrayMenuItem(1, "Open")], alternate);

    [Fact]
    public async Task A_model_that_asks_for_the_second_picture_is_added_with_it()
    {
        (await _icon.ShowAsync(Model(alternate: true), Ct)).IsSuccess.ShouldBeTrue();

        _api.Added.Single().Icon.ShouldBe(Alternate);
    }

    [Fact]
    public async Task A_model_that_does_not_is_added_with_the_normal_picture()
    {
        await _icon.ShowAsync(Model(alternate: false), Ct);

        _api.Added.Single().Icon.ShouldBe(Normal);
        _api.LoadedFrom.ShouldBe([IconPath, AlternatePath]);
    }

    [Fact]
    public async Task Switching_changes_the_picture_and_only_when_the_choice_changes()
    {
        await _icon.ShowAsync(Model(alternate: false), Ct);

        await _icon.UpdateAsync(Model(alternate: false), Ct);
        _api.IconChanges.ShouldBeEmpty();

        await _icon.UpdateAsync(Model(alternate: true), Ct);
        await _icon.UpdateAsync(Model(alternate: true), Ct);
        await _icon.UpdateAsync(Model(alternate: false), Ct);

        _api.IconChanges.ShouldBe([Alternate, Normal]);
    }

    [Fact]
    public async Task A_second_picture_that_cannot_be_loaded_falls_back_to_the_normal_one_without_an_error()
    {
        _api.AlternateIconHandle = 0;

        var shown = await _icon.ShowAsync(Model(alternate: true), Ct);
        var updated = await _icon.UpdateAsync(Model(alternate: false), Ct);
        await _icon.UpdateAsync(Model(alternate: true), Ct);

        shown.IsSuccess.ShouldBeTrue();
        updated.IsSuccess.ShouldBeTrue();
        _api.Added.Single().Icon.ShouldBe(Normal);
        _api.IconChanges.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_picture_change_windows_refuses_is_reported()
    {
        await _icon.ShowAsync(Model(alternate: false), Ct);
        _api.ChangeSucceeds = false;

        var result = await _icon.UpdateAsync(Model(alternate: true), Ct);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task When_the_taskbar_is_created_again_the_icon_comes_back_with_the_current_picture()
    {
        await _icon.ShowAsync(Model(alternate: false), Ct);
        await _icon.UpdateAsync(Model(alternate: true), Ct);

        _window.Send(_api.TaskbarCreatedMessage);

        _api.Added[1].Icon.ShouldBe(Alternate);
    }

    [Fact]
    public async Task Disposing_frees_both_pictures()
    {
        await _icon.ShowAsync(Model(alternate: false), Ct);

        _icon.Dispose();

        _api.Destroyed.ShouldBe([Normal, Alternate], ignoreOrder: true);
    }

    [Fact]
    public async Task Without_a_second_picture_the_normal_one_is_always_used()
    {
        var api = new FakeTrayApi(_window);
        using var icon = new TrayIcon(_window, _window, Handle, api, new FakeTrayMenu(_window), IconPath);

        await icon.ShowAsync(Model(alternate: true), Ct);
        await icon.UpdateAsync(Model(alternate: false), Ct);

        api.Added.Single().Icon.ShouldBe(Normal);
        api.IconChanges.ShouldBeEmpty();
        api.LoadedFrom.ShouldBe([IconPath]);
    }
}
