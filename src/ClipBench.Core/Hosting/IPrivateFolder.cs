using ClipBench.Core.Results;

namespace ClipBench.Core.Hosting;

/// <summary>Makes a folder readable and writable by the current user and the operating system only.</summary>
public interface IPrivateFolder
{
    /// <summary>
    /// Creates the folder when it is missing and gives the current user and SYSTEM full access to it and everything in
    /// it, with no other entry and nothing taken from the folder above.
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <returns>Success, or <see cref="ErrorCode.FolderNotPrivate"/>.</returns>
    Result Secure(string path);
}
