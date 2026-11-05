using System.ComponentModel;
using ClipBench.Core.Hosting;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Preferences;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Preferences;

public sealed class GeneralSettingsViewModelTests : IDisposable
{
    private const string Path = @"C:\Program Files\ClipBench\ClipBench.App.exe";
    private const string Quoted = "\"C:\\Program Files\\ClipBench\\ClipBench.App.exe\"";

    private readonly FakeStartup _registration = new();
    private readonly FakeSettingsStore _store = new();
    private readonly ImmediateDispatcher _dispatcher = new();
    private readonly SettingsService _settings;
    private readonly GeneralSettingsViewModel _vm;

    public GeneralSettingsViewModelTests()
    {
        _settings = new SettingsService(_store);
        _vm = new GeneralSettingsViewModel(_settings, new StartWithWindowsService(_registration, _settings, Path), _dispatcher);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _vm.Dispose();
        _settings.Dispose();
    }

    [Fact]
    public void Starting_with_Windows_is_off_and_the_first_run_question_is_waiting()
    {
        _vm.StartWithWindows.ShouldBeFalse();
        _vm.ShowFirstRun.ShouldBeTrue();
        _vm.CanStartWithWindows.ShouldBeTrue();
        _vm.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Turning_it_on_writes_the_entry_and_the_switch_shows_it()
    {
        await _vm.SetStartWithWindowsAsync(true, Ct);

        _registration.Command.ShouldBe(Quoted);
        _vm.StartWithWindows.ShouldBeTrue();
        _vm.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Turning_it_off_removes_the_entry()
    {
        await _vm.SetStartWithWindowsAsync(true, Ct);

        await _vm.SetStartWithWindowsAsync(false, Ct);

        _registration.Command.ShouldBeNull();
        _vm.StartWithWindows.ShouldBeFalse();
    }

    [Fact]
    public async Task The_switch_in_the_window_does_the_same_as_the_method()
    {
        _vm.StartWithWindows = true;
        await Task.Yield();

        _registration.Command.ShouldBe(Quoted);
        _vm.StartWithWindows.ShouldBeTrue();
    }

    [Fact]
    public async Task When_windows_refuses_the_switch_snaps_back_and_the_reason_is_shown()
    {
        _registration.Failure = new ResultError(ErrorCode.PlatformFailure, "Windows would not let ClipBench add itself.");
        var raised = new List<string?>();
        _vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await _vm.SetStartWithWindowsAsync(true, Ct);

        _vm.StartWithWindows.ShouldBeFalse();
        _vm.ErrorMessage.ShouldBe("Windows would not let ClipBench add itself.");
        raised.ShouldContain(string.Empty);
    }

    [Fact]
    public async Task A_new_try_clears_the_old_error()
    {
        _registration.Failure = new ResultError(ErrorCode.PlatformFailure, "no");
        await _vm.SetStartWithWindowsAsync(true, Ct);
        _registration.Failure = null;

        await _vm.SetStartWithWindowsAsync(true, Ct);

        _vm.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task Without_a_known_program_path_the_choice_cannot_be_made_and_the_reason_is_shown()
    {
        using var vm = new GeneralSettingsViewModel(_settings, startup: null, _dispatcher);

        await vm.SetStartWithWindowsAsync(true, Ct);

        vm.CanStartWithWindows.ShouldBeFalse();
        vm.StartWithWindows.ShouldBeFalse();
        vm.ErrorMessage.ShouldBe(GeneralSettingsViewModel.StartupUnavailable);
    }

    // --- the first-run question ---

    [Fact]
    public async Task Saying_yes_at_first_run_starts_with_Windows_and_ends_the_question()
    {
        await _vm.AcceptFirstRunCommand.ExecuteAsync(null);

        _registration.Command.ShouldBe(Quoted);
        _settings.Current.StartWithWindows.ShouldBeTrue();
        _settings.Current.FirstRunCompleted.ShouldBeTrue();
        _vm.ShowFirstRun.ShouldBeFalse();
    }

    [Fact]
    public async Task Saying_no_at_first_run_changes_nothing_but_ends_the_question()
    {
        await _vm.DeclineFirstRunCommand.ExecuteAsync(null);

        _registration.Command.ShouldBeNull();
        _settings.Current.StartWithWindows.ShouldBeFalse();
        _settings.Current.FirstRunCompleted.ShouldBeTrue();
        _vm.ShowFirstRun.ShouldBeFalse();
    }

    [Fact]
    public async Task A_yes_that_windows_refuses_keeps_the_question_so_it_can_be_asked_again()
    {
        _registration.Failure = new ResultError(ErrorCode.PlatformFailure, "refused");

        await _vm.AcceptFirstRunCommand.ExecuteAsync(null);

        _settings.Current.FirstRunCompleted.ShouldBeFalse();
        _vm.ShowFirstRun.ShouldBeTrue();
        _vm.ErrorMessage.ShouldBe("refused");
    }

    [Fact]
    public async Task A_yes_where_starting_with_Windows_is_not_possible_ends_the_question_and_says_why()
    {
        using var vm = new GeneralSettingsViewModel(_settings, startup: null, _dispatcher);

        await vm.AcceptFirstRunCommand.ExecuteAsync(null);

        _settings.Current.FirstRunCompleted.ShouldBeTrue();
        vm.ErrorMessage.ShouldBe(GeneralSettingsViewModel.StartupUnavailable);
    }

    // --- following the settings ---

    [Fact]
    public async Task A_change_made_elsewhere_makes_every_binding_read_again()
    {
        var raised = new List<string?>();
        _vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await _settings.UpdateAsync(settings => settings with { FirstRunCompleted = true }, Ct);

        raised.ShouldContain(string.Empty);
        _vm.ShowFirstRun.ShouldBeFalse();
    }

    [Fact]
    public async Task After_it_is_disposed_it_no_longer_follows_the_settings()
    {
        var raised = 0;
        _vm.PropertyChanged += (_, _) => raised++;
        _vm.Dispose();

        await _settings.UpdateAsync(settings => settings with { FirstRunCompleted = true }, Ct);

        raised.ShouldBe(0);
    }

    [Fact]
    public void Missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new GeneralSettingsViewModel(null!, null, _dispatcher));
        Should.Throw<ArgumentNullException>(() => new GeneralSettingsViewModel(_settings, null, null!));
    }

    private sealed class FakeStartup : IStartupRegistration
    {
        public string? Command { get; private set; }

        public ResultError? Failure { get; set; }

        public Result<string?> GetCommand() => Failure is { } error ? Result.Failure<string?>(error) : Result.Success(Command);

        public Result SetCommand(string command)
        {
            if (Failure is { } error)
            {
                return Result.Failure(error);
            }

            Command = command;
            return Result.Success();
        }

        public Result Remove()
        {
            if (Failure is { } error)
            {
                return Result.Failure(error);
            }

            Command = null;
            return Result.Success();
        }
    }
}
