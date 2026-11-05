using System.Security;
using ClipBench.Core.Hosting;
using ClipBench.Core.Results;
using Microsoft.Win32;

namespace ClipBench.Platform.Windows.Hosting;

/// <summary>
/// Keeps ClipBench's entry in the signed-in user's own <c>Run</c> key. That key needs no administrator rights and holds
/// nothing but the program path, so no clip content or setting is ever written to the registry. Only the one value is
/// ever touched.
/// </summary>
public sealed class RegistryStartupRegistration : IStartupRegistration
{
    /// <summary>The key Windows reads at sign-in, under the current user's hive.</summary>
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The name of ClipBench's value.</summary>
    public const string DefaultValueName = "ClipBench";

    private readonly string _keyPath;
    private readonly string _valueName;

    /// <summary>Uses the real <c>Run</c> key and ClipBench's value.</summary>
    public RegistryStartupRegistration()
        : this(RunKeyPath, DefaultValueName)
    {
    }

    /// <summary>Uses another key and value name, so that tests never touch the real one.</summary>
    /// <param name="keyPath">The key under the current user's hive.</param>
    /// <param name="valueName">The name of the value.</param>
    public RegistryStartupRegistration(string keyPath, string valueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        _keyPath = keyPath;
        _valueName = valueName;
    }

    /// <inheritdoc/>
    public Result<string?> GetCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath);

            // A value that is not text is not ours: an empty command never matches, so the next write replaces it.
            return Result.Success(key?.GetValue(_valueName) switch
            {
                null => null,
                string text => text,
                _ => string.Empty,
            });
        }
        catch (Exception exception) when (IsRegistryProblem(exception))
        {
            return Result.Failure<string?>(Failure("ClipBench could not read the list of programs that start with Windows."));
        }
    }

    /// <inheritdoc/>
    public Result SetCommand(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
            key.SetValue(_valueName, command, RegistryValueKind.String);
            return Result.Success();
        }
        catch (Exception exception) when (IsRegistryProblem(exception))
        {
            return Result.Failure(Failure("Windows would not let ClipBench add itself to the programs that start with Windows."));
        }
    }

    /// <inheritdoc/>
    public Result Remove()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
            key?.DeleteValue(_valueName, throwOnMissingValue: false);
            return Result.Success();
        }
        catch (Exception exception) when (IsRegistryProblem(exception))
        {
            return Result.Failure(Failure("Windows would not let ClipBench remove itself from the programs that start with Windows."));
        }
    }

    private static bool IsRegistryProblem(Exception exception) =>
        exception is SecurityException or UnauthorizedAccessException or IOException;

    private static ResultError Failure(string message) => new(ErrorCode.PlatformFailure, message);
}
