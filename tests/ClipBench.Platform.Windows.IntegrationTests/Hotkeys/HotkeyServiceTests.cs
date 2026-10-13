using System.Collections.Concurrent;
using ClipBench.Core.Hotkeys;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Hotkeys;
using ClipBench.Platform.Windows.IntegrationTests.Support;

namespace ClipBench.Platform.Windows.IntegrationTests.Hotkeys;

public sealed class HotkeyServiceTests : IDisposable
{
    private const uint HotkeyMessage = 0x0312;   // WM_HOTKEY

    private readonly FakeWindow _window = new();
    private readonly FakeHotkeyApi _api;
    private readonly HotkeyService _service;
    private readonly BlockingCollection<(HotkeyId Id, int Thread)> _pressed = [];

    public HotkeyServiceTests()
    {
        _api = new FakeHotkeyApi(_window);
        _service = new HotkeyService(_window, _window, _api);
        _service.Pressed += (_, args) => _pressed.Add((args.Id, Environment.CurrentManagedThreadId));
    }

    public void Dispose()
    {
        _service.Dispose();
        _pressed.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HotkeyId> RegisterAsync(string hotkey)
    {
        var result = await _service.RegisterAsync(Hotkey.Parse(hotkey).Value, Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    private (HotkeyId Id, int Thread) NextPress() =>
        _pressed.TryTake(out var item, 10_000, Ct) ? item : throw new TimeoutException("no press arrived");

    private bool NoMorePresses() => !_pressed.TryTake(out _, 150, Ct);

    // --- registering ---

    [Theory]
    [InlineData("Win+Alt+V", 8u | 1u | 0x4000u, 0x56u)]
    [InlineData("Ctrl+Shift+Space", 2u | 4u | 0x4000u, 0x20u)]
    [InlineData("Ctrl+Alt+Shift+Win+F12", 2u | 1u | 4u | 8u | 0x4000u, 0x7Bu)]
    [InlineData("Ctrl+K", 2u | 0x4000u, 0x4Bu)]
    [InlineData("F24", 0x4000u, 0x87u)]
    public async Task A_shortcut_is_claimed_with_Windows_modifier_flags_and_without_key_repeat(string hotkey, uint modifiers, uint key)
    {
        var id = await RegisterAsync(hotkey);

        _api.Registered.ShouldBe([(id.Value, modifiers, key)]);
    }

    [Fact]
    public async Task Every_registration_gets_its_own_number()
    {
        var ids = new[] { await RegisterAsync("Ctrl+1"), await RegisterAsync("Ctrl+2"), await RegisterAsync("Ctrl+3") };

        ids.Distinct().Count().ShouldBe(3);
        ids.ShouldAllBe(id => id.Value >= 1 && id.Value <= 0xBFFF, "Windows only allows numbers up to 0xBFFF");
    }

    [Fact]
    public async Task A_shortcut_another_program_has_is_reported_as_unavailable_by_name()
    {
        _api.FailWith = 1409;   // ERROR_HOTKEY_ALREADY_REGISTERED

        var result = await _service.RegisterAsync(Hotkey.Default, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.HotkeyUnavailable);
        result.Error.Message.ShouldContain("Win+Alt+V");
        result.Error.Message.ShouldContain("another");
    }

    [Fact]
    public async Task Any_other_Windows_error_is_a_platform_failure_that_names_the_error()
    {
        _api.FailWith = 5;

        var result = await _service.RegisterAsync(Hotkey.Default, Ct);

        result.Error.Code.ShouldBe(ErrorCode.PlatformFailure);
        result.Error.Message.ShouldContain("5");
    }

    [Fact]
    public async Task A_shortcut_that_failed_to_register_is_not_remembered()
    {
        _api.FailWith = 1409;
        var failed = await _service.RegisterAsync(Hotkey.Default, Ct);

        _window.Send(HotkeyMessage, (nint)1);

        failed.IsFailure.ShouldBeTrue();
        NoMorePresses().ShouldBeTrue();
    }

    [Fact]
    public async Task Running_out_of_numbers_is_reported_and_not_wrapped_around()
    {
        using var small = new HotkeyService(_window, _window, _api, firstId: 0xBFFE);
        (await small.RegisterAsync(Hotkey.Parse("Ctrl+1").Value, Ct)).IsSuccess.ShouldBeTrue();
        (await small.RegisterAsync(Hotkey.Parse("Ctrl+2").Value, Ct)).IsSuccess.ShouldBeTrue();

        var result = await small.RegisterAsync(Hotkey.Parse("Ctrl+3").Value, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.PlatformFailure);
    }

    [Fact]
    public async Task Numbers_are_handed_out_in_turn_so_a_released_one_is_not_reused_at_once()
    {
        // A press of a released shortcut can still be waiting in the queue. Giving its number straight to a different
        // shortcut would send that press to the wrong place.
        var first = await RegisterAsync("Ctrl+K");
        await _service.UnregisterAsync(first, Ct);

        var second = await RegisterAsync("Ctrl+L");

        second.ShouldNotBe(first);
    }

    [Fact]
    public async Task Released_numbers_come_round_again_so_registering_over_and_over_never_runs_out()
    {
        // Queue paste releases and claims Ctrl+V around every pasted item. Only two numbers exist here.
        using var small = new HotkeyService(_window, _window, _api, firstId: 0xBFFE);
        var current = (await small.RegisterAsync(Hotkey.Parse("Ctrl+K").Value, Ct)).Value;

        for (var i = 0; i < 200; i++)
        {
            (await small.UnregisterAsync(current, Ct)).IsSuccess.ShouldBeTrue();
            var next = await small.RegisterAsync(Hotkey.Parse("Ctrl+K").Value, Ct);
            next.IsSuccess.ShouldBeTrue($"round {i}");
            current = next.Value;
        }
    }

    [Fact]
    public async Task A_number_in_use_is_never_handed_out_again()
    {
        using var small = new HotkeyService(_window, _window, _api, firstId: 0xBFFE);
        var one = (await small.RegisterAsync(Hotkey.Parse("Ctrl+1").Value, Ct)).Value;
        var two = (await small.RegisterAsync(Hotkey.Parse("Ctrl+2").Value, Ct)).Value;
        await small.UnregisterAsync(one, Ct);

        var three = (await small.RegisterAsync(Hotkey.Parse("Ctrl+3").Value, Ct)).Value;

        three.ShouldBe(one, "the only free number");
        three.ShouldNotBe(two);
    }

    [Fact]
    public async Task A_number_for_a_registration_that_failed_is_free_again()
    {
        using var small = new HotkeyService(_window, _window, _api, firstId: 0xBFFE);
        _api.FailWith = 1409;
        (await small.RegisterAsync(Hotkey.Default, Ct)).IsFailure.ShouldBeTrue();
        _api.FailWith = 0;

        // Both numbers are free: the failed one was not kept.
        (await small.RegisterAsync(Hotkey.Parse("Ctrl+1").Value, Ct)).IsSuccess.ShouldBeTrue();
        (await small.RegisterAsync(Hotkey.Parse("Ctrl+2").Value, Ct)).IsSuccess.ShouldBeTrue();
    }
    [Fact]
    public async Task Releasing_a_number_makes_room_again_when_all_of_them_were_in_use()
    {
        using var small = new HotkeyService(_window, _window, _api, firstId: 0xBFFE);
        var one = (await small.RegisterAsync(Hotkey.Parse("Ctrl+1").Value, Ct)).Value;
        (await small.RegisterAsync(Hotkey.Parse("Ctrl+2").Value, Ct)).IsSuccess.ShouldBeTrue();
        (await small.RegisterAsync(Hotkey.Parse("Ctrl+3").Value, Ct)).IsFailure.ShouldBeTrue();

        await small.UnregisterAsync(one, Ct);

        (await small.RegisterAsync(Hotkey.Parse("Ctrl+3").Value, Ct)).IsSuccess.ShouldBeTrue();
    }
    // --- presses ---

    [Fact]
    public async Task A_press_names_the_shortcut_and_arrives_off_the_window_thread()
    {
        var id = await RegisterAsync("Ctrl+K");

        _window.Send(HotkeyMessage, id.Value).ShouldBeTrue();

        var (pressed, thread) = NextPress();
        pressed.ShouldBe(id);
        thread.ShouldNotBe(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public async Task Presses_of_several_shortcuts_arrive_in_order()
    {
        var one = await RegisterAsync("Ctrl+1");
        var two = await RegisterAsync("Ctrl+2");

        _window.Send(HotkeyMessage, two.Value);
        _window.Send(HotkeyMessage, one.Value);
        _window.Send(HotkeyMessage, two.Value);

        NextPress().Id.ShouldBe(two);
        NextPress().Id.ShouldBe(one);
        NextPress().Id.ShouldBe(two);
    }

    [Fact]
    public async Task A_message_for_a_number_that_was_not_registered_here_is_ignored()
    {
        await RegisterAsync("Ctrl+K");

        _window.Send(HotkeyMessage, 777).ShouldBeFalse("it is not ours, so the window procedure may pass it on");

        NoMorePresses().ShouldBeTrue();
    }

    [Fact]
    public async Task A_subscriber_that_throws_does_not_stop_later_presses()
    {
        var id = await RegisterAsync("Ctrl+K");
        var other = new ConcurrentQueue<HotkeyId>();
        _service.Pressed += (_, _) => throw new InvalidOperationException("a subscriber bug");
        _service.Pressed += (_, args) => other.Enqueue(args.Id);

        _window.Send(HotkeyMessage, id.Value);
        _window.Send(HotkeyMessage, id.Value);

        NextPress();
        NextPress();
        SpinWait.SpinUntil(() => other.Count == 2, TimeSpan.FromSeconds(10)).ShouldBeTrue();
    }

    // --- releasing ---

    [Fact]
    public async Task A_released_shortcut_is_given_back_to_Windows_and_its_presses_are_ignored()
    {
        var id = await RegisterAsync("Ctrl+K");

        var released = await _service.UnregisterAsync(id, Ct);
        _window.Send(HotkeyMessage, id.Value);

        released.IsSuccess.ShouldBeTrue();
        _api.Released.ShouldBe([id.Value]);
        NoMorePresses().ShouldBeTrue();
    }

    [Fact]
    public async Task Releasing_a_number_that_is_not_registered_is_a_failure_and_asks_Windows_nothing()
    {
        var unknown = await _service.UnregisterAsync(new HotkeyId(42), Ct);
        var id = await RegisterAsync("Ctrl+K");
        await _service.UnregisterAsync(id, Ct);
        var twice = await _service.UnregisterAsync(id, Ct);

        unknown.Error.Code.ShouldBe(ErrorCode.HotkeyNotRegistered);
        twice.Error.Code.ShouldBe(ErrorCode.HotkeyNotRegistered);
        _api.Released.Count.ShouldBe(1);
    }

    [Fact]
    public async Task If_Windows_will_not_release_a_shortcut_it_stays_registered_and_the_failure_is_reported()
    {
        var id = await RegisterAsync("Ctrl+K");
        _api.RefuseUnregister = true;

        var result = await _service.UnregisterAsync(id, Ct);

        result.Error.Code.ShouldBe(ErrorCode.PlatformFailure);
        _window.Send(HotkeyMessage, id.Value).ShouldBeTrue("the shortcut is still ours");
        NextPress().Id.ShouldBe(id);
    }

    [Fact]
    public async Task A_shortcut_can_be_claimed_again_after_it_was_released()
    {
        var first = await RegisterAsync("Ctrl+K");
        await _service.UnregisterAsync(first, Ct);

        var second = await RegisterAsync("Ctrl+K");

        second.ShouldNotBe(first);
        _api.Registered.Count.ShouldBe(2);
    }

    // --- disposing ---

    [Fact]
    public async Task Disposing_gives_every_shortcut_back_and_stops_presses_and_new_registrations()
    {
        var one = await RegisterAsync("Ctrl+1");
        var two = await RegisterAsync("Ctrl+2");

        _service.Dispose();
        _window.Send(HotkeyMessage, one.Value);

        _api.Released.Order().ShouldBe([one.Value, two.Value]);
        NoMorePresses().ShouldBeTrue();
        await Should.ThrowAsync<ObjectDisposedException>(_service.RegisterAsync(Hotkey.Default, Ct));
        await Should.ThrowAsync<ObjectDisposedException>(_service.UnregisterAsync(one, Ct));
    }

    [Fact]
    public void Disposing_twice_is_fine()
    {
        _service.Dispose();

        Should.NotThrow(_service.Dispose);
    }
}
