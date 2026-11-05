using ClipBench.Core.Capture;
using ClipBench.Core.History;
using ClipBench.Core.Privacy;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Preferences;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Preferences;

public sealed class PrivacySettingsViewModelTests : IDisposable
{
    private readonly FakeSettingsStore _store = new();
    private readonly ImmediateDispatcher _dispatcher = new();
    private readonly FakeCapture _capture = new();
    private readonly FakeWipe _wipe = new();
    private readonly SettingsService _settings;
    private readonly PrivacySettingsViewModel _vm;

    public PrivacySettingsViewModelTests()
    {
        _settings = new SettingsService(_store);
        _vm = new PrivacySettingsViewModel(_settings, _capture, _wipe, _dispatcher);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _vm.Dispose();
        _settings.Dispose();
    }

    // --- secrets and the screen ---

    [Fact]
    public void It_starts_with_the_default_privacy_choices()
    {
        _vm.SensitiveExpirySeconds.ShouldBe(120);
        _vm.MaskSensitive.ShouldBeTrue();
        _vm.HideFromScreenCapture.ShouldBeTrue();
        _vm.IsIncognito.ShouldBeFalse();
        PrivacySettingsViewModel.SensitiveExpirySecondsRange.ShouldBe(AppSettings.SensitiveExpirySecondsRange);
    }

    [Fact]
    public async Task The_choices_are_saved_and_a_number_outside_its_range_is_moved_to_the_nearest_end()
    {
        _vm.SensitiveExpirySeconds = 600;
        _vm.MaskSensitive = false;
        _vm.HideFromScreenCapture = false;
        await Task.Yield();

        _settings.Current.SensitiveExpirySeconds.ShouldBe(600);
        _settings.Current.MaskSensitive.ShouldBeFalse();
        _settings.Current.HideFromScreenCapture.ShouldBeFalse();

        _vm.SensitiveExpirySeconds = 1;
        await Task.Yield();
        _vm.SensitiveExpirySeconds.ShouldBe(15);
    }

    // --- incognito ---

    [Fact]
    public void Turning_incognito_on_pauses_capture_and_off_resumes_it()
    {
        _vm.IsIncognito = true;
        _capture.IsPaused.ShouldBeTrue();

        _vm.IsIncognito = false;
        _capture.IsPaused.ShouldBeFalse();
    }

    [Fact]
    public void Incognito_turned_on_elsewhere_for_example_in_the_tray_shows_here()
    {
        var raised = new List<string?>();
        _vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _capture.SetPaused(true);

        _vm.IsIncognito.ShouldBeTrue();
        raised.ShouldContain(string.Empty);
    }

    [Fact]
    public void After_it_is_disposed_it_no_longer_follows_capture()
    {
        var raised = 0;
        _vm.PropertyChanged += (_, _) => raised++;
        _vm.Dispose();

        _capture.SetPaused(true);

        raised.ShouldBe(0);
    }

    // --- excluded programs ---

    [Fact]
    public void It_lists_the_default_excluded_programs()
    {
        _vm.ExcludedPrograms.ShouldBe(ExclusionList.Default.Names);
    }

    [Fact]
    public async Task Adding_a_program_saves_it_at_the_end_and_clears_the_box()
    {
        _vm.NewProgram = @"C:\Tools\MyVault.exe";

        await _vm.AddProgramCommand.ExecuteAsync(null);

        _vm.ExcludedPrograms[^1].ShouldBe("MyVault");
        _settings.Current.ExcludedPrograms.Contains("myvault.exe").ShouldBeTrue();
        _vm.NewProgram.ShouldBeEmpty();
        _vm.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task A_name_that_cannot_be_a_program_is_refused_with_the_reason_and_stays_in_the_box()
    {
        _vm.NewProgram = "a*b";

        await _vm.AddProgramCommand.ExecuteAsync(null);

        _vm.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
        _vm.NewProgram.ShouldBe("a*b");
        _vm.ExcludedPrograms.ShouldBe(ExclusionList.Default.Names);
    }

    [Fact]
    public async Task Adding_a_program_that_is_already_listed_changes_nothing_and_clears_the_box()
    {
        _vm.NewProgram = "keepass.exe";

        await _vm.AddProgramCommand.ExecuteAsync(null);

        _vm.ExcludedPrograms.ShouldBe(ExclusionList.Default.Names);
        _vm.NewProgram.ShouldBeEmpty();
    }

    [Fact]
    public async Task Adding_with_an_empty_box_says_to_type_a_name()
    {
        _vm.NewProgram = "   ";

        await _vm.AddProgramCommand.ExecuteAsync(null);

        _vm.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Removing_a_program_takes_it_off_the_list()
    {
        await _vm.RemoveProgramCommand.ExecuteAsync("KeePass");

        _vm.ExcludedPrograms.ShouldNotContain("KeePass");
        _settings.Current.ExcludedPrograms.Contains("KeePass.exe").ShouldBeFalse();
        _vm.ExcludedPrograms.Length.ShouldBe(ExclusionList.Default.Count - 1);
    }

    [Fact]
    public async Task The_list_can_be_emptied_and_put_back_to_the_default()
    {
        foreach (var name in ExclusionList.Default.Names)
        {
            await _vm.RemoveProgramCommand.ExecuteAsync(name);
        }

        _vm.ExcludedPrograms.ShouldBeEmpty();

        await _vm.RestoreDefaultExclusionsCommand.ExecuteAsync(null);

        _vm.ExcludedPrograms.ShouldBe(ExclusionList.Default.Names);
    }

    [Fact]
    public async Task A_list_that_cannot_be_saved_still_applies_and_shows_the_reason()
    {
        _store.Failure = new ResultError(ErrorCode.StorageFailure, "Your settings could not be saved.");
        _vm.NewProgram = "tool";

        await _vm.AddProgramCommand.ExecuteAsync(null);

        _vm.ExcludedPrograms.ShouldContain("tool");
        _vm.ErrorMessage.ShouldBe("Your settings could not be saved.");
    }

    // --- wipe all ---

    [Fact]
    public void Nothing_is_wiped_until_it_is_confirmed()
    {
        _vm.RequestWipeCommand.Execute(null);

        _vm.IsConfirmingWipe.ShouldBeTrue();
        _wipe.Wipes.ShouldBe(0);
    }

    [Fact]
    public void Cancelling_the_confirmation_wipes_nothing()
    {
        _vm.RequestWipeCommand.Execute(null);

        _vm.CancelWipeCommand.Execute(null);

        _vm.IsConfirmingWipe.ShouldBeFalse();
        _wipe.Wipes.ShouldBe(0);
    }

    [Fact]
    public async Task Confirming_wipes_everything_and_says_so()
    {
        _vm.RequestWipeCommand.Execute(null);

        await _vm.ConfirmWipeCommand.ExecuteAsync(null);

        _wipe.Wipes.ShouldBe(1);
        _vm.IsConfirmingWipe.ShouldBeFalse();
        _vm.IsWiping.ShouldBeFalse();
        _vm.WipeStatus.ShouldBe(PrivacySettingsViewModel.WipeDone);
    }

    [Fact]
    public async Task Confirming_without_asking_first_wipes_nothing()
    {
        await _vm.ConfirmWipeCommand.ExecuteAsync(null);

        _wipe.Wipes.ShouldBe(0);
    }

    [Fact]
    public async Task A_wipe_that_failed_says_why_and_does_not_claim_success()
    {
        _wipe.Failure = new ResultError(ErrorCode.StorageFailure, "The history could not be wiped.");
        _vm.RequestWipeCommand.Execute(null);

        await _vm.ConfirmWipeCommand.ExecuteAsync(null);

        _vm.WipeStatus.ShouldBe("The history could not be wiped.");
        _vm.IsConfirmingWipe.ShouldBeFalse();
    }

    [Fact]
    public async Task While_the_wipe_runs_the_section_says_so_and_a_second_confirmation_does_nothing()
    {
        var release = new TaskCompletionSource();
        _wipe.Hold = release.Task;
        _vm.RequestWipeCommand.Execute(null);

        var first = _vm.ConfirmWipeCommand.ExecuteAsync(null);
        _vm.IsWiping.ShouldBeTrue();
        _vm.RequestWipeCommand.Execute(null);
        var second = _vm.ConfirmWipeCommand.ExecuteAsync(null);

        release.SetResult();
        await first;
        await second;

        _wipe.Wipes.ShouldBe(1);
        _vm.IsWiping.ShouldBeFalse();
    }

    [Fact]
    public async Task A_new_request_clears_the_old_status()
    {
        _vm.RequestWipeCommand.Execute(null);
        await _vm.ConfirmWipeCommand.ExecuteAsync(null);

        _vm.RequestWipeCommand.Execute(null);

        _vm.WipeStatus.ShouldBeNull();
    }

    [Fact]
    public void Missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new PrivacySettingsViewModel(null!, _capture, _wipe, _dispatcher));
        Should.Throw<ArgumentNullException>(() => new PrivacySettingsViewModel(_settings, null!, _wipe, _dispatcher));
        Should.Throw<ArgumentNullException>(() => new PrivacySettingsViewModel(_settings, _capture, null!, _dispatcher));
        Should.Throw<ArgumentNullException>(() => new PrivacySettingsViewModel(_settings, _capture, _wipe, null!));
    }

    private sealed class FakeCapture : ICaptureControl
    {
        public bool IsPaused { get; private set; }

        public event EventHandler? PausedChanged;

        public void SetPaused(bool paused)
        {
            if (IsPaused != paused)
            {
                IsPaused = paused;
                PausedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private sealed class FakeWipe : IWipeAll
    {
        public int Wipes { get; private set; }

        public ResultError? Failure { get; set; }

        public Task? Hold { get; set; }

        public async Task<Result> WipeAllAsync(CancellationToken cancellationToken)
        {
            Wipes++;
            if (Hold is { } hold)
            {
                await hold;
            }

            return Failure is { } error ? Result.Failure(error) : Result.Success();
        }
    }
}
