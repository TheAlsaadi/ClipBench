using System.Collections.Immutable;
using ClipBench.Core.Hotkeys;

namespace ClipBench.Presentation.Hosting;

/// <summary>
/// What the command line can change. There are two switches and both exist so that tests can run the app without
/// touching the user's history or taking the user's shortcut. Anyone who can start the app with arguments can already
/// run code as the user, so they give nobody more reach.
/// </summary>
public sealed record StartupOptions
{
    /// <summary>The switch that moves the history and the key to another folder.</summary>
    public const string DataDirectorySwitch = "--data-dir";

    /// <summary>The switch that replaces the popup shortcut.</summary>
    public const string HotkeySwitch = "--hotkey";

    /// <summary>The switch that lets other programs capture the popup in screenshots. Only for testing: the popup hides itself from capture otherwise.</summary>
    public const string AllowScreenCaptureSwitch = "--allow-screen-capture";

    /// <summary>The folder for the history, its key and the log, or null for the default.</summary>
    public string? DataDirectory { get; init; }

    /// <summary>The popup shortcut, or null for the default.</summary>
    public Hotkey? Hotkey { get; init; }

    /// <summary>True to leave the popup visible to screenshots and screen sharing, which it hides from by default.</summary>
    public bool AllowScreenCapture { get; init; }

    /// <summary>What was wrong with the arguments, in plain words. Wrong arguments are ignored, never fatal.</summary>
    public ImmutableArray<string> Problems { get; init; } = [];

    /// <summary>Reads the command line.</summary>
    /// <param name="args">The arguments, without the program name.</param>
    /// <returns>The options, with a note for each argument that was ignored.</returns>
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? dataDirectory = null;
        Hotkey? hotkey = null;
        var allowScreenCapture = false;
        var problems = ImmutableArray.CreateBuilder<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var argument = args[i];
            var equals = argument.IndexOf('=', StringComparison.Ordinal);
            var name = equals < 0 ? argument : argument[..equals];
            var isDataDirectory = name.Equals(DataDirectorySwitch, StringComparison.OrdinalIgnoreCase);
            var isHotkey = name.Equals(HotkeySwitch, StringComparison.OrdinalIgnoreCase);

            if (name.Equals(AllowScreenCaptureSwitch, StringComparison.OrdinalIgnoreCase))
            {
                if (equals >= 0)
                {
                    problems.Add($"{AllowScreenCaptureSwitch} takes no value, so it was ignored.");
                }
                else
                {
                    allowScreenCapture = true;
                }

                continue;
            }

            if (!isDataDirectory && !isHotkey)
            {
                problems.Add($"The argument {argument} is not known, so it was ignored.");
                continue;
            }

            var switchName = isDataDirectory ? DataDirectorySwitch : HotkeySwitch;
            string? value;
            if (equals >= 0)
            {
                value = argument[(equals + 1)..];
            }
            else if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }
            else
            {
                value = null;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"{switchName} needs a value, so it was ignored.");
            }
            else if (isDataDirectory)
            {
                if (value.StartsWith(@"\\", StringComparison.Ordinal))
                {
                    // A network share or a device path: the app makes no network calls, and the system would make them for it.
                    problems.Add($"{DataDirectorySwitch} needs a folder on this computer, so it was ignored.");
                }
                else if (Path.IsPathFullyQualified(value))
                {
                    dataDirectory = value;
                }
                else
                {
                    problems.Add($"{DataDirectorySwitch} needs a full path, so it was ignored.");
                }
            }
            else
            {
                var parsed = Core.Hotkeys.Hotkey.Parse(value);
                if (parsed.IsSuccess)
                {
                    hotkey = parsed.Value;
                }
                else
                {
                    problems.Add($"{HotkeySwitch} is not a shortcut ClipBench understands, so it was ignored.");
                }
            }
        }

        return new StartupOptions { DataDirectory = dataDirectory, Hotkey = hotkey, AllowScreenCapture = allowScreenCapture, Problems = problems.ToImmutable() };
    }
}
