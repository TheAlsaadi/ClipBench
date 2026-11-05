using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Preferences;

/// <summary>Tells the app to open an address in the user's own browser.</summary>
/// <param name="url">The address.</param>
public sealed class OpenAddressEventArgs(string url) : EventArgs
{
    /// <summary>The address to open.</summary>
    public string Url { get; } = url;
}

/// <summary>The About section: what ClipBench is, who it is for, and its promise not to use the network.</summary>
public sealed partial class AboutViewModel : ObservableObject
{
    /// <summary>The project's page.</summary>
    public const string Repository = "https://github.com/TheAlsaadi/ClipBench";

    /// <summary>Creates the section.</summary>
    /// <param name="version">The version of the program, as shown.</param>
    public AboutViewModel(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        Version = version;
    }

    /// <summary>Raised when the user chose to open the project's page. The app opens it; ClipBench itself never connects.</summary>
    public event EventHandler<OpenAddressEventArgs>? OpenRepositoryRequested;

    /// <summary>The name of the program.</summary>
    public static string Title => "ClipBench";

    /// <summary>The version of the program.</summary>
    public string Version { get; }

    /// <summary>The license, in words.</summary>
    public static string License => "ClipBench is free and open source, under the MIT License.";

    /// <summary>The promise about the network.</summary>
    public static string NoNetworkPromise =>
        "ClipBench makes no network connections at all: no telemetry, no update checks and no cloud sync. Everything stays on this PC, encrypted.";

    /// <summary>The project's page.</summary>
    public static string RepositoryUrl => Repository;

    [RelayCommand]
    private void OpenRepository() => OpenRepositoryRequested?.Invoke(this, new OpenAddressEventArgs(Repository));
}
