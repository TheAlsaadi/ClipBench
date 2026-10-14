using ClipBench.Core.Hotkeys;
using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.Hotkeys;
using ClipBench.Platform.Windows.Pasting;
using ClipBench.Platform.Windows.Screen;
using ClipBench.Platform.Windows.Security;

namespace ClipBench.Platform.Windows.IntegrationTests;

public class WindowsPlatformTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WindowsPlatform Create()
    {
        var created = WindowsPlatform.Create(TimeProvider.System);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : string.Empty);
        return created.Value;
    }

    [Fact]
    public void Every_service_is_the_real_implementation_behind_its_core_interface()
    {
        using var platform = Create();

        platform.ClipboardListener.ShouldBeOfType<ClipboardListener>();
        platform.ClipboardReader.ShouldBeOfType<ClipboardReader>();
        platform.ClipboardWriter.ShouldBeOfType<ClipboardWriter>();
        platform.Hotkeys.ShouldBeOfType<HotkeyService>();
        platform.ForegroundTracker.ShouldBeOfType<ForegroundTracker>();
        platform.PasteInjector.ShouldBeOfType<PasteInjector>();
        platform.ScreenLocator.ShouldBeOfType<ScreenLocator>();
        platform.WindowEffects.ShouldBeOfType<Win32WindowEffects>();
    }

    [Fact]
    public void The_key_protector_is_the_windows_account_one()
    {
        WindowsPlatform.CreateKeyProtector().ShouldBeOfType<DpapiKeyProtector>();
    }

    private static string NewFolder() => Path.Combine(Path.GetTempPath(), "clipbench-instance-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void The_first_copy_for_a_data_folder_is_the_running_one_and_a_second_is_not()
    {
        var folder = NewFolder();
        using var first = WindowsPlatform.AcquireSingleInstance(folder);
        using var second = WindowsPlatform.AcquireSingleInstance(folder);

        first.IsFirstInstance.ShouldBeTrue();
        second.IsFirstInstance.ShouldBeFalse();
    }

    [Fact]
    public void The_same_folder_written_differently_is_still_the_same_folder()
    {
        var folder = NewFolder();
        using var first = WindowsPlatform.AcquireSingleInstance(folder);
        using var second = WindowsPlatform.AcquireSingleInstance(folder.ToUpperInvariant() + @"\");

        second.IsFirstInstance.ShouldBeFalse();
    }

    [Fact]
    public void Copies_that_use_different_data_folders_do_not_get_in_each_others_way()
    {
        using var first = WindowsPlatform.AcquireSingleInstance(NewFolder());
        using var second = WindowsPlatform.AcquireSingleInstance(NewFolder());

        first.IsFirstInstance.ShouldBeTrue();
        second.IsFirstInstance.ShouldBeTrue();
    }

    [Fact]
    public void A_folder_name_that_is_not_a_full_path_is_refused()
    {
        Should.Throw<ArgumentException>(() => WindowsPlatform.AcquireSingleInstance(@"relative\folder"));
        Should.Throw<ArgumentException>(() => WindowsPlatform.AcquireSingleInstance(""));
    }

    [Fact]
    public void Disposing_twice_is_fine()
    {
        var platform = Create();

        platform.Dispose();

        Should.NotThrow(platform.Dispose);
    }

    [Fact]
    public void Two_platforms_can_exist_at_once_each_with_its_own_window()
    {
        using var first = Create();
        using var second = Create();

        first.Hotkeys.ShouldNotBeSameAs(second.Hotkeys);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_hotkey_service_works_on_the_platform_window()
    {
        using var platform = Create();
        var f24 = Hotkey.Parse("F24").Value;

        var registered = await platform.Hotkeys.RegisterAsync(f24, Ct);

        // Another program may own F24; that is a refusal, not a fault of the platform.
        if (registered.IsSuccess)
        {
            (await platform.Hotkeys.UnregisterAsync(registered.Value, Ct)).IsSuccess.ShouldBeTrue();
        }
        else
        {
            registered.Error.Code.ShouldBe(Core.Results.ErrorCode.HotkeyUnavailable);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task The_clipboard_listener_starts_on_the_platform_window()
    {
        using var platform = Create();

        var started = await platform.ClipboardListener.StartAsync(Ct);

        started.IsSuccess.ShouldBeTrue();
    }
}
