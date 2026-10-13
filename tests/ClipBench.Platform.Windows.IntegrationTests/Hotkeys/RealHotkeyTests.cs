using System.Collections.Concurrent;
using ClipBench.Core.Hotkeys;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Hotkeys;
using ClipBench.Platform.Windows.IntegrationTests.Support;
using ClipBench.Platform.Windows.Windowing;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace ClipBench.Platform.Windows.IntegrationTests.Hotkeys;

/// <summary>
/// Real RegisterHotKey on the real operating system. The presses are F24 and F23, keys no keyboard has, and a
/// registered shortcut takes them before any program sees them, so nothing is typed anywhere.
/// </summary>
[Collection("Hotkeys")]
[Trait("Category", "Integration")]
public sealed class RealHotkeyTests : IDisposable
{
    private static readonly Hotkey _f24 = Hotkey.Parse("F24").Value;
    private static readonly Hotkey _f23 = Hotkey.Parse("F23").Value;

    private readonly MessageWindow _window = MessageWindow.Create().Value;
    private readonly HotkeyService _service;
    private readonly BlockingCollection<(HotkeyId Id, int Thread)> _pressed = [];

    public RealHotkeyTests()
    {
        _service = new HotkeyService(_window, _window, new Win32HotkeyApi(_window.Handle));
        _service.Pressed += (_, args) => _pressed.Add((args.Id, Environment.CurrentManagedThreadId));
    }

    public void Dispose()
    {
        _service.Dispose();
        _window.Dispose();
        _pressed.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HotkeyId> RegisterOrSkipAsync(HotkeyService service, Hotkey hotkey)
    {
        var result = await service.RegisterAsync(hotkey, Ct);
        if (result.IsFailure && result.Error.Code == ErrorCode.HotkeyUnavailable)
        {
            Assert.Skip($"{hotkey} is already used by another program on this machine, so this test cannot run here.");
        }

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    private (HotkeyId Id, int Thread) NextPress() =>
        _pressed.TryTake(out var item, 10_000, Ct) ? item : throw new TimeoutException("the key press never arrived");

    private bool NoMorePresses() => !_pressed.TryTake(out _, 500, Ct);

    [Fact]
    public async Task A_real_key_press_reaches_the_service_with_the_registered_number_off_the_window_thread()
    {
        var id = await RegisterOrSkipAsync(_service, _f24);

        Keyboard.Tap(VIRTUAL_KEY.VK_F24);

        var (pressed, thread) = NextPress();
        pressed.ShouldBe(id);
        thread.ShouldNotBe(Environment.CurrentManagedThreadId);
        NoMorePresses().ShouldBeTrue("one press is one event, and holding is not repeated");
    }

    [Fact]
    public async Task Two_shortcuts_are_told_apart()
    {
        var first = await RegisterOrSkipAsync(_service, _f24);
        var second = await RegisterOrSkipAsync(_service, _f23);

        Keyboard.Tap(VIRTUAL_KEY.VK_F23);
        Keyboard.Tap(VIRTUAL_KEY.VK_F24);

        NextPress().Id.ShouldBe(second);
        NextPress().Id.ShouldBe(first);
    }

    [Fact]
    public async Task A_shortcut_already_taken_is_reported_as_unavailable_naming_it()
    {
        await RegisterOrSkipAsync(_service, _f24);
        using var otherWindow = MessageWindow.Create().Value;
        using var other = new HotkeyService(otherWindow, otherWindow, new Win32HotkeyApi(otherWindow.Handle));

        var result = await other.RegisterAsync(_f24, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.HotkeyUnavailable);
        result.Error.Message.ShouldContain("F24");
    }

    [Fact]
    public async Task A_released_shortcut_no_longer_reaches_the_service_and_can_be_taken_by_someone_else()
    {
        var f24 = await RegisterOrSkipAsync(_service, _f24);
        var f23 = await RegisterOrSkipAsync(_service, _f23);
        (await _service.UnregisterAsync(f24, Ct)).IsSuccess.ShouldBeTrue();

        Keyboard.Tap(VIRTUAL_KEY.VK_F24);
        Keyboard.Tap(VIRTUAL_KEY.VK_F23);

        NextPress().Id.ShouldBe(f23, "the key that is still registered proves the presses were sent");
        NoMorePresses().ShouldBeTrue("the released key produced nothing");

        using var otherWindow = MessageWindow.Create().Value;
        using var other = new HotkeyService(otherWindow, otherWindow, new Win32HotkeyApi(otherWindow.Handle));
        (await other.RegisterAsync(_f24, Ct)).IsSuccess.ShouldBeTrue("Windows gave the shortcut back");
    }

    [Fact]
    public async Task Disposing_the_service_gives_its_shortcuts_back_to_Windows()
    {
        await RegisterOrSkipAsync(_service, _f24);

        _service.Dispose();

        using var otherWindow = MessageWindow.Create().Value;
        using var other = new HotkeyService(otherWindow, otherWindow, new Win32HotkeyApi(otherWindow.Handle));
        (await other.RegisterAsync(_f24, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task The_default_shortcut_is_either_registered_or_reported_as_unavailable_never_a_crash()
    {
        var result = await _service.RegisterAsync(Hotkey.Default, Ct);

        if (result.IsFailure)
        {
            result.Error.Code.ShouldBe(ErrorCode.HotkeyUnavailable);
            result.Error.Message.ShouldContain("Win+Alt+V");
        }
    }
}

[CollectionDefinition("Hotkeys", DisableParallelization = true)]
public sealed class HotkeyTestGroup;
