namespace ClipBench.Storage.Tests;

public class StoragePathsTests
{
    private static readonly string _root = Path.Combine(Path.GetTempPath(), "ClipBench.Tests.Paths");

    [Fact]
    public void The_database_and_the_key_live_directly_under_the_root()
    {
        var paths = new StoragePaths(_root);

        paths.Root.ShouldBe(_root);
        paths.DatabasePath.ShouldBe(Path.Combine(_root, "clipbench.db"));
        paths.KeyPath.ShouldBe(Path.Combine(_root, "key.bin"));
    }

    [Fact]
    public void The_settings_live_directly_under_the_root()
    {
        new StoragePaths(_root).SettingsPath.ShouldBe(Path.Combine(_root, "settings.json"));
    }

    [Fact]
    public void The_default_root_is_the_per_user_local_application_data_folder()
    {
        var paths = StoragePaths.ForCurrentUser();

        paths.Root.ShouldBe(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipBench"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/folder")]
    public void A_root_that_is_blank_or_not_absolute_is_a_bug(string root)
    {
        Should.Throw<ArgumentException>(() => new StoragePaths(root));
    }

    [Fact]
    public void A_missing_root_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new StoragePaths(null!));
    }

    [Fact]
    public void A_root_is_normalized()
    {
        new StoragePaths(Path.Combine(_root, "a", "..", "b")).Root.ShouldBe(Path.Combine(_root, "b"));
    }
}
