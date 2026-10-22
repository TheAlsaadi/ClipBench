using ClipBench.Core.Results;

namespace ClipBench.Core.Settings;

/// <summary>How a settings load went.</summary>
public enum SettingsLoadStatus
{
    /// <summary>The saved settings were read.</summary>
    Loaded,

    /// <summary>Nothing was saved yet, so the defaults are used.</summary>
    Missing,

    /// <summary>The saved settings were damaged or too large. The defaults are used and the bad file was set aside.</summary>
    Damaged,

    /// <summary>The saved settings could not be read, for example because another program holds the file. The defaults are used and the file is left alone.</summary>
    Unreadable,
}

/// <summary>What a settings load produced.</summary>
/// <param name="Settings">The settings to use.</param>
/// <param name="Status">How they were found.</param>
public readonly record struct SettingsLoad(AppSettings Settings, SettingsLoadStatus Status);

/// <summary>Keeps the settings between runs. Calls are not made concurrently.</summary>
public interface ISettingsStore
{
    /// <summary>Reads the saved settings. It never fails: anything that cannot be read gives the defaults.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The settings and how they were found.</returns>
    Task<SettingsLoad> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Saves the settings, replacing what was saved.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>Success, or <see cref="ErrorCode.StorageFailure"/> when they could not be written.</returns>
    Task<Result> SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
