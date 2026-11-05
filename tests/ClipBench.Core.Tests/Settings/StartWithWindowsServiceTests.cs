using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Settings;

public sealed class StartWithWindowsServiceTests : IDisposable
{
    private const string Path = @"C:\Program Files\ClipBench\ClipBench.App.exe";
    private const string Quoted = "\"C:\\Program Files\\ClipBench\\ClipBench.App.exe\"";

    private static readonly ResultError _registryError = new(ErrorCode.PlatformFailure, "Windows said no");

    private readonly FakeStartupRegistration _registration = new();
    private readonly FakeSettingsStore _store = new();
    private readonly SettingsService _settings;
    private readonly StartWithWindowsService _service;

    public StartWithWindowsServiceTests()
    {
        _settings = new SettingsService(_store);
        _service = new StartWithWindowsService(_registration, _settings, Path);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _settings.Dispose();

    // --- turning it on ---

    [Fact]
    public async Task Turning_it_on_registers_the_quoted_program_path_and_saves_the_choice()
    {
        var result = await _service.SetAsync(true, Ct);

        result.IsSuccess.ShouldBeTrue();
        _registration.Command.ShouldBe(Quoted);
        _settings.Current.StartWithWindows.ShouldBeTrue();
        _store.Saved.Last().StartWithWindows.ShouldBeTrue();
    }

    [Fact]
    public async Task Turning_it_on_when_it_is_already_registered_writes_nothing()
    {
        _registration.Command = Quoted;

        await _service.SetAsync(true, Ct);

        _registration.Written.ShouldBeEmpty();
    }

    [Fact]
    public async Task Turning_it_on_when_another_path_is_registered_replaces_it()
    {
        _registration.Command = "\"D:\\old\\ClipBench.App.exe\"";

        await _service.SetAsync(true, Ct);

        _registration.Written.ShouldBe([Quoted]);
    }

    [Fact]
    public async Task When_windows_refuses_the_entry_the_choice_is_not_saved_and_the_reason_is_returned()
    {
        _registration.Failure = _registryError;

        var result = await _service.SetAsync(true, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_registryError);
        _settings.Current.StartWithWindows.ShouldBeFalse();
        _store.Saved.ShouldBeEmpty();
    }

    // --- turning it off ---

    [Fact]
    public async Task Turning_it_off_removes_the_entry_and_saves_the_choice()
    {
        _registration.Command = Quoted;
        await _settings.UpdateAsync(settings => settings with { StartWithWindows = true }, Ct);

        var result = await _service.SetAsync(false, Ct);

        result.IsSuccess.ShouldBeTrue();
        _registration.Command.ShouldBeNull();
        _settings.Current.StartWithWindows.ShouldBeFalse();
    }

    [Fact]
    public async Task Turning_it_off_when_nothing_is_registered_removes_nothing()
    {
        var result = await _service.SetAsync(false, Ct);

        result.IsSuccess.ShouldBeTrue();
        _registration.Removals.ShouldBe(0);
    }

    [Fact]
    public async Task Turning_it_off_when_windows_refuses_keeps_the_choice_on()
    {
        _registration.Command = Quoted;
        await _settings.UpdateAsync(settings => settings with { StartWithWindows = true }, Ct);
        _registration.Failure = _registryError;

        var result = await _service.SetAsync(false, Ct);

        result.IsFailure.ShouldBeTrue();
        _settings.Current.StartWithWindows.ShouldBeTrue();
    }

    // --- a setting that cannot be saved ---

    [Fact]
    public async Task A_choice_that_cannot_be_saved_is_reported_after_the_entry_was_written()
    {
        _store.Failure = new ResultError(ErrorCode.StorageFailure, "the disk said no");

        var result = await _service.SetAsync(true, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.StorageFailure);
        _registration.Command.ShouldBe(Quoted);
    }

    // --- keeping the entry right at start-up ---

    [Fact]
    public async Task At_start_up_a_chosen_entry_that_is_missing_is_written_again()
    {
        await _settings.UpdateAsync(settings => settings with { StartWithWindows = true }, Ct);

        var result = _service.Reconcile();

        result.IsSuccess.ShouldBeTrue();
        _registration.Command.ShouldBe(Quoted);
    }

    [Fact]
    public async Task At_start_up_an_entry_that_points_to_where_the_program_used_to_be_is_corrected()
    {
        await _settings.UpdateAsync(settings => settings with { StartWithWindows = true }, Ct);
        _registration.Command = "\"D:\\moved\\ClipBench.App.exe\"";

        _service.Reconcile();

        _registration.Command.ShouldBe(Quoted);
    }

    [Fact]
    public async Task At_start_up_a_correct_entry_is_left_alone()
    {
        await _settings.UpdateAsync(settings => settings with { StartWithWindows = true }, Ct);
        _registration.Command = Quoted;

        _service.Reconcile();

        _registration.Written.ShouldBeEmpty();
    }

    [Fact]
    public void At_start_up_with_the_choice_off_nothing_is_written_and_nothing_is_removed()
    {
        _registration.Command = "\"C:\\other\\thing.exe\"";

        var result = _service.Reconcile();

        result.IsSuccess.ShouldBeTrue();
        _registration.Command.ShouldBe("\"C:\\other\\thing.exe\"");
        _registration.Written.ShouldBeEmpty();
        _registration.Removals.ShouldBe(0);
    }

    [Fact]
    public async Task At_start_up_a_registry_that_cannot_be_used_is_reported()
    {
        await _settings.UpdateAsync(settings => settings with { StartWithWindows = true }, Ct);
        _registration.Failure = _registryError;

        _service.Reconcile().Error.ShouldBe(_registryError);
    }

    // --- what is registered ---

    [Fact]
    public async Task It_tells_whether_the_program_is_registered_to_start()
    {
        _service.IsRegistered().Value.ShouldBeFalse();

        await _service.SetAsync(true, Ct);
        _service.IsRegistered().Value.ShouldBeTrue();

        _registration.Command = "\"D:\\elsewhere\\ClipBench.App.exe\"";
        _service.IsRegistered().Value.ShouldBeFalse();
    }

    // --- mistakes ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ClipBench.App.exe")]
    [InlineData("C:\\a\"b\\c.exe")]
    public void A_path_that_is_blank_relative_or_holds_a_quote_is_a_bug(string path)
    {
        Should.Throw<ArgumentException>(() => new StartWithWindowsService(_registration, _settings, path));
    }

    [Fact]
    public void Missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new StartWithWindowsService(null!, _settings, Path));
        Should.Throw<ArgumentNullException>(() => new StartWithWindowsService(_registration, null!, Path));
        Should.Throw<ArgumentNullException>(() => new StartWithWindowsService(_registration, _settings, null!));
    }
}
