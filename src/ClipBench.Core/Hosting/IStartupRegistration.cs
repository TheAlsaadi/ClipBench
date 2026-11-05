using ClipBench.Core.Results;

namespace ClipBench.Core.Hosting;

/// <summary>
/// The list of programs Windows starts when the user signs in. ClipBench has at most one entry in it, for the signed-in
/// user only: it never needs administrator rights and never touches the entries of other programs.
/// </summary>
public interface IStartupRegistration
{
    /// <summary>Reads ClipBench's entry.</summary>
    /// <returns>The command Windows would run, or null when there is no entry, or why it could not be read.</returns>
    Result<string?> GetCommand();

    /// <summary>Writes ClipBench's entry, replacing an earlier one.</summary>
    /// <param name="command">The command to run, with the program path in quotes.</param>
    /// <returns>Success, or <see cref="ErrorCode.PlatformFailure"/> when Windows would not let the entry be written.</returns>
    Result SetCommand(string command);

    /// <summary>Removes ClipBench's entry. Succeeds when there is none.</summary>
    /// <returns>Success, or <see cref="ErrorCode.PlatformFailure"/> when Windows would not let the entry be removed.</returns>
    Result Remove();
}
