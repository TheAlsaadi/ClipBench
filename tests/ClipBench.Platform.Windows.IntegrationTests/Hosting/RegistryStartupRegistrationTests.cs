using ClipBench.Platform.Windows.Hosting;
using Microsoft.Win32;

namespace ClipBench.Platform.Windows.IntegrationTests.Hosting;

/// <summary>
/// Runs against a throwaway key under the current user's own hive, never the real Run key, and removes it afterwards.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RegistryStartupRegistrationTests : IDisposable
{
    private const string ValueName = "ClipBenchTest";
    private const string ParentPath = @"Software\ClipBenchTests";

    private readonly string _keyPath = $@"{ParentPath}\Run-{Guid.NewGuid():N}";
    private readonly RegistryStartupRegistration _registration;

    public RegistryStartupRegistrationTests() => _registration = new RegistryStartupRegistration(_keyPath, ValueName);

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);

        // The shared parent goes too once the last test is done with it; while another test still uses it, it stays.
        using var parent = Registry.CurrentUser.OpenSubKey(ParentPath);
        if (parent is { SubKeyCount: 0, ValueCount: 0 })
        {
            try
            {
                Registry.CurrentUser.DeleteSubKey(ParentPath, throwOnMissingSubKey: false);
            }
            catch (InvalidOperationException)
            {
                // Another test created a key in it between the check and the delete.
            }
        }
    }

    private string? Raw()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
        return key?.GetValue(ValueName) as string;
    }

    [Fact]
    public void With_no_key_and_no_entry_nothing_is_registered()
    {
        var command = _registration.GetCommand();

        command.IsSuccess.ShouldBeTrue();
        command.Value.ShouldBeNull();
    }

    [Fact]
    public void A_command_that_is_written_is_read_back_and_stored_as_text_under_the_value_name()
    {
        const string Command = "\"C:\\Program Files\\ClipBench\\ClipBench.App.exe\"";

        _registration.SetCommand(Command).IsSuccess.ShouldBeTrue();

        _registration.GetCommand().Value.ShouldBe(Command);
        Raw().ShouldBe(Command);
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath)!;
        key.GetValueKind(ValueName).ShouldBe(RegistryValueKind.String);
    }

    [Fact]
    public void Writing_again_replaces_the_entry()
    {
        _registration.SetCommand("\"C:\\old.exe\"");

        _registration.SetCommand("\"C:\\new.exe\"").IsSuccess.ShouldBeTrue();

        _registration.GetCommand().Value.ShouldBe("\"C:\\new.exe\"");
    }

    [Fact]
    public void Removing_deletes_only_this_programs_entry()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_keyPath))
        {
            key.SetValue("SomeoneElse", "\"C:\\other.exe\"");
        }

        _registration.SetCommand("\"C:\\clipbench.exe\"");

        _registration.Remove().IsSuccess.ShouldBeTrue();

        _registration.GetCommand().Value.ShouldBeNull();
        using var after = Registry.CurrentUser.OpenSubKey(_keyPath)!;
        after.GetValue("SomeoneElse").ShouldBe("\"C:\\other.exe\"");
    }

    [Fact]
    public void Removing_when_there_is_nothing_to_remove_succeeds()
    {
        _registration.Remove().IsSuccess.ShouldBeTrue();

        _registration.SetCommand("\"C:\\x.exe\"");
        _registration.Remove().IsSuccess.ShouldBeTrue();
        _registration.Remove().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void An_entry_that_is_not_text_reads_as_a_command_that_is_not_ours_so_it_gets_replaced()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_keyPath))
        {
            key.SetValue(ValueName, 7, RegistryValueKind.DWord);
        }

        var command = _registration.GetCommand();

        command.IsSuccess.ShouldBeTrue();
        command.Value.ShouldBe(string.Empty);
        _registration.SetCommand("\"C:\\x.exe\"").IsSuccess.ShouldBeTrue();
        _registration.GetCommand().Value.ShouldBe("\"C:\\x.exe\"");
    }

    [Fact]
    public void The_default_entry_is_in_the_current_users_own_run_key()
    {
        RegistryStartupRegistration.RunKeyPath.ShouldBe(@"Software\Microsoft\Windows\CurrentVersion\Run");
        RegistryStartupRegistration.DefaultValueName.ShouldBe("ClipBench");
    }

    [Fact]
    public void A_blank_command_is_a_bug()
    {
        Should.Throw<ArgumentException>(() => _registration.SetCommand(" "));
        Should.Throw<ArgumentNullException>(() => _registration.SetCommand(null!));
    }

    [Fact]
    public void A_blank_key_or_value_name_is_a_bug()
    {
        Should.Throw<ArgumentException>(() => new RegistryStartupRegistration(" ", ValueName));
        Should.Throw<ArgumentException>(() => new RegistryStartupRegistration(_keyPath, " "));
    }
}
