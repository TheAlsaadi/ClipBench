using ClipBench.Core.Hosting;
using ClipBench.Core.Results;

namespace ClipBench.Core.Settings;

/// <summary>
/// Starts ClipBench when the user signs in to Windows, if they chose that. The choice is saved only once Windows has
/// accepted the entry, so the setting never claims something that is not in place.
/// </summary>
public sealed class StartWithWindowsService
{
    private readonly IStartupRegistration _registration;
    private readonly SettingsService _settings;
    private readonly string _command;

    /// <summary>Creates the service.</summary>
    /// <param name="registration">Windows' list of programs to start.</param>
    /// <param name="settings">Where the choice is kept.</param>
    /// <param name="executablePath">The full path of the program to start, which is quoted for the entry.</param>
    public StartWithWindowsService(IStartupRegistration registration, SettingsService settings, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath) || executablePath.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("The program path must be a full path without quotes.", nameof(executablePath));
        }

        _registration = registration;
        _settings = settings;
        _command = $"\"{executablePath}\"";
    }

    /// <summary>Tells whether Windows has an entry that starts this program.</summary>
    /// <returns>True when the entry is there and points here, or why it could not be read.</returns>
    public Result<bool> IsRegistered()
    {
        var current = _registration.GetCommand();
        return current.IsFailure ? Result.Failure<bool>(current.Error) : Result.Success(current.Value == _command);
    }

    /// <summary>Turns starting with Windows on or off.</summary>
    /// <param name="enabled">True to start with Windows.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>
    /// Success, or why it failed. When Windows refuses the entry nothing changes. When only the save fails the entry is in
    /// place and the choice is in force for this run.
    /// </returns>
    public async Task<Result> SetAsync(bool enabled, CancellationToken cancellationToken)
    {
        var applied = Apply(enabled);
        if (applied.IsFailure)
        {
            return applied;
        }

        return await _settings.UpdateAsync(settings => settings with { StartWithWindows = enabled }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes the entry match the choice at start-up: a chosen entry that is missing, or that points to where the program
    /// used to be, is written again. With the choice off nothing is touched, because the entry may be someone else's.
    /// </summary>
    /// <returns>Success, or why the entry could not be checked or written.</returns>
    public Result Reconcile() => _settings.Current.StartWithWindows ? Apply(enabled: true) : Result.Success();

    private Result Apply(bool enabled)
    {
        var current = _registration.GetCommand();
        if (current.IsFailure)
        {
            return Result.Failure(current.Error);
        }

        if (enabled)
        {
            return current.Value == _command ? Result.Success() : _registration.SetCommand(_command);
        }

        return current.Value is null ? Result.Success() : _registration.Remove();
    }
}
