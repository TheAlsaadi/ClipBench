using System.Security.AccessControl;
using System.Security.Principal;
using ClipBench.Platform.Windows.Hosting;

namespace ClipBench.Platform.Windows.IntegrationTests.Hosting;

public sealed class PrivateFolderTests : IDisposable
{
    private static readonly SecurityIdentifier _system = new(WellKnownSidType.LocalSystemSid, null);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "clipbench-private-" + Guid.NewGuid().ToString("N"));
    private readonly SecurityIdentifier _user = WindowsIdentity.GetCurrent().User!;

    public PrivateFolderTests() => _ = Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Folder => Path.Combine(_root, "ClipBench");

    private static List<FileSystemAccessRule> Rules(FileSystemSecurity security) =>
        [.. security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()];

    [Fact]
    public void Secure_leaves_only_the_user_and_SYSTEM_with_access()
    {
        _ = Directory.CreateDirectory(Folder);

        new PrivateFolder().Secure(Folder).IsSuccess.ShouldBeTrue();

        var security = new DirectoryInfo(Folder).GetAccessControl();
        security.AreAccessRulesProtected.ShouldBeTrue("the folder must not take rules from the folder above it");
        var rules = Rules(security);
        rules.Select(rule => (SecurityIdentifier)rule.IdentityReference).ShouldBe([_user, _system], ignoreOrder: true);
        rules.ShouldAllBe(rule =>
            rule.AccessControlType == AccessControlType.Allow
            && rule.FileSystemRights == FileSystemRights.FullControl
            && rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)
            && !rule.IsInherited);
    }

    [Fact]
    public void A_file_created_after_Secure_inherits_the_same_access()
    {
        new PrivateFolder().Secure(Folder).IsSuccess.ShouldBeTrue();
        var file = Path.Combine(Folder, "clipbench.db");
        File.WriteAllBytes(file, [1, 2, 3]);

        var rules = Rules(new FileInfo(file).GetAccessControl());

        rules.Select(rule => (SecurityIdentifier)rule.IdentityReference).Distinct().ShouldBe([_user, _system], ignoreOrder: true);
    }

    [Fact]
    public void Files_and_folders_already_inside_get_the_same_access()
    {
        var logs = Directory.CreateDirectory(Path.Combine(Folder, "logs"));
        var file = Path.Combine(logs.FullName, "clipbench.log");
        File.WriteAllText(file, "x");

        new PrivateFolder().Secure(Folder).IsSuccess.ShouldBeTrue();

        Rules(new FileInfo(file).GetAccessControl()).Select(rule => (SecurityIdentifier)rule.IdentityReference).Distinct()
            .ShouldBe([_user, _system], ignoreOrder: true);
        Rules(logs.GetAccessControl()).Select(rule => (SecurityIdentifier)rule.IdentityReference).Distinct()
            .ShouldBe([_user, _system], ignoreOrder: true);
    }

    [Fact]
    public void Secure_on_a_folder_that_does_not_exist_creates_it()
    {
        new PrivateFolder().Secure(Folder).IsSuccess.ShouldBeTrue();

        Directory.Exists(Folder).ShouldBeTrue();
    }

    [Fact]
    public void Secure_twice_changes_nothing()
    {
        var folder = new PrivateFolder();
        folder.Secure(Folder).IsSuccess.ShouldBeTrue();
        var first = new DirectoryInfo(Folder).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);

        folder.Secure(Folder).IsSuccess.ShouldBeTrue();

        new DirectoryInfo(Folder).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access).ShouldBe(first);
    }

    [Fact]
    public void A_folder_that_cannot_be_made_fails_without_throwing()
    {
        // A file stands where the folder should be.
        File.WriteAllText(Folder, "in the way");

        var result = new PrivateFolder().Secure(Folder);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(Core.Results.ErrorCode.FolderNotPrivate);
    }
}
