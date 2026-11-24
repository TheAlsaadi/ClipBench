using System.Security.AccessControl;
using System.Security.Principal;
using ClipBench.Core.Hosting;
using ClipBench.Core.Results;

namespace ClipBench.Platform.Windows.Hosting;

/// <summary>
/// Gives the history folder an access list of its own: the current user and SYSTEM, full control, inherited by every
/// file and folder inside. Nothing comes from the folder above, so a redirected or shared <c>%LOCALAPPDATA%</c> cannot
/// open the history to other accounts. The content is encrypted either way; this keeps the files themselves private.
/// </summary>
public sealed class PrivateFolder : IPrivateFolder
{
    private const InheritanceFlags ToEverythingInside = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    /// <inheritdoc/>
    public Result Secure(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User ?? throw new UnauthorizedAccessException("The current account has no security identifier.");
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, ToEverythingInside, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, ToEverythingInside, PropagationFlags.None, AccessControlType.Allow));

            var folder = Directory.CreateDirectory(path);
            folder.SetAccessControl(security);
            return Result.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Result.Failure(new ResultError(ErrorCode.FolderNotPrivate, "ClipBench could not make its history folder private to you."));
        }
    }
}
