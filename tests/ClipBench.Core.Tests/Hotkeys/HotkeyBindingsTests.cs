using ClipBench.Core.Hotkeys;
using ClipBench.Core.Results;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Hotkeys;

public sealed class HotkeyBindingsTests : IDisposable
{
    private static readonly Hotkey _popup = new(HotkeyModifiers.Windows | HotkeyModifiers.Alt, 'V');
    private static readonly Hotkey _other = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'B');
    private static readonly Hotkey _collect = new(HotkeyModifiers.Windows | HotkeyModifiers.Alt, 'C');

    private readonly FakeHotkeyService _service = new();
    private readonly HotkeyBindings _bindings;

    public HotkeyBindingsTests() => _bindings = new HotkeyBindings(_service);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _bindings.Dispose();

    // --- binding ---

    [Fact]
    public async Task Nothing_is_bound_at_first()
    {
        _bindings.Current(HotkeyAction.OpenPopup).ShouldBeNull();
        _bindings.Current(HotkeyAction.ToggleCollect).ShouldBeNull();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Binding_claims_the_shortcut_and_remembers_it()
    {
        var result = await _bindings.BindAsync(HotkeyAction.OpenPopup, _popup, Ct);

        result.IsSuccess.ShouldBeTrue();
        _bindings.Current(HotkeyAction.OpenPopup).ShouldBe(_popup);
        _service.IsRegistered(_popup).ShouldBeTrue();
    }

    [Fact]
    public async Task A_shortcut_that_another_program_holds_is_refused_and_nothing_is_bound()
    {
        _service.Taken.Add(_popup);

        var result = await _bindings.BindAsync(HotkeyAction.OpenPopup, _popup, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.HotkeyUnavailable);
        _bindings.Current(HotkeyAction.OpenPopup).ShouldBeNull();
    }

    // --- changing ---

    [Fact]
    public async Task Changing_a_shortcut_claims_the_new_one_before_it_gives_the_old_one_back()
    {
        await _bindings.BindAsync(HotkeyAction.OpenPopup, _popup, Ct);

        var result = await _bindings.BindAsync(HotkeyAction.OpenPopup, _other, Ct);

        result.IsSuccess.ShouldBeTrue();
        _bindings.Current(HotkeyAction.OpenPopup).ShouldBe(_other);
        _service.Calls.ShouldBe(["+" + _popup, "+" + _other, "-" + _popup]);
        _service.IsRegistered(_popup).ShouldBeFalse();
    }

    [Fact]
    public async Task A_new_shortcut_another_program_holds_leaves_the_old_one_working()
    {
        await _bindings.BindAsync(HotkeyAction.OpenPopup, _popup, Ct);
        _service.Taken.Add(_other);

        var result = await _bindings.BindAsync(HotkeyAction.OpenPopup, _other, Ct);

        result.IsFailure.ShouldBeTrue();
        _bindings.Current(HotkeyAction.OpenPopup).ShouldBe(_popup);
        _service.IsRegistered(_popup).ShouldBeTrue();
    }

    [Fact]
    public async Task Binding_the_same_shortcut_again_does_nothing()
    {
        await _bindings.BindAsync(HotkeyAction.OpenPopup, _popup, Ct);
        _service.Calls.Clear();

        var result = await _bindings.BindAsync(HotkeyAction.OpenPopup, _popup, Ct);

        result.IsSuccess.ShouldBeTrue();
        _service.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task One_shortcut_cannot_do_two_things()
    {
        await _bindings.BindAsync(HotkeyAction.OpenPopup, _popup, Ct);

        var result = await _bindings.BindAsync(HotkeyAction.ToggleCollect, _popup, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.HotkeyInvalid);
        _bindings.Current(HotkeyAction.ToggleCollect).ShouldBeNull();
        _service.Calls.ShouldBe(["+" + _popup]);
    }

    [Fact]
    public async Task The_two_actions_keep_their_own_shortcuts()
    {
        await _bindings.BindAsync(HotkeyAction.OpenPopup, _popup, Ct);
        await _bindings.BindAsync(HotkeyAction.ToggleCollect, _collect, Ct);

        _bindings.Current(HotkeyAction.OpenPopup).ShouldBe(_popup);
        _bindings.Current(HotkeyAction.ToggleCollect).ShouldBe(_collect);
    }

    // --- pressing ---

    [Fact]
    public async Task A_press_is_reported_as_the_action_that_owns_the_shortcut()
    {
        var pressed = new List<HotkeyAction>();
        _bindings.Pressed += (_, e) => pressed.Add(e.Action);
        await _bindings.BindAsync(HotkeyAction.OpenPopup, _popup, Ct);
        await _bindings.BindAsync(HotkeyAction.ToggleCollect, _collect, Ct);

        _service.Press(_collect);
        _service.Press(_popup);

        pressed.ShouldBe([HotkeyAction.ToggleCollect, HotkeyAction.OpenPopup]);
    }

    [Fact]
    public async Task After_a_change_only_the_new_shortcut_counts()
    {
        var pressed = new List<HotkeyAction>();
        _bindings.Pressed += (_, e) => pressed.Add(e.Action);
        await _bindings.BindAsync(HotkeyAction.OpenPopup, _popup, Ct);
        await _bindings.BindAsync(HotkeyAction.OpenPopup, _other, Ct);

        _service.Press(_popup);
        _service.Press(_other);

        pressed.ShouldBe([HotkeyAction.OpenPopup]);
    }

    [Fact]
    public async Task A_listener_that_throws_does_not_stop_the_others()
    {
        var reached = false;
        _bindings.Pressed += (_, _) => throw new InvalidOperationException("a bug in a listener");
        _bindings.Pressed += (_, _) => reached = true;
        await _bindings.BindAsync(HotkeyAction.OpenPopup, _popup, Ct);

        _service.Press(_popup);

        reached.ShouldBeTrue();
    }

    [Fact]
    public async Task After_it_is_disposed_presses_are_ignored()
    {
        var pressed = 0;
        _bindings.Pressed += (_, _) => pressed++;
        await _bindings.BindAsync(HotkeyAction.OpenPopup, _popup, Ct);
        _bindings.Dispose();

        _service.Press(_popup);

        pressed.ShouldBe(0);
    }

    [Fact]
    public async Task A_missing_service_or_shortcut_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new HotkeyBindings(null!));
        await Should.ThrowAsync<ArgumentNullException>(() => _bindings.BindAsync(HotkeyAction.OpenPopup, null!, Ct));
    }
}
