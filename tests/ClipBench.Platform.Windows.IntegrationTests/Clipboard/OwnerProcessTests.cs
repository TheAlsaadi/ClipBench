using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.IntegrationTests.Support;

namespace ClipBench.Platform.Windows.IntegrationTests.Clipboard;

public class OwnerProcessTests
{
    private readonly FakeProcessLookup _lookup = new();

    [Fact]
    public void The_program_is_named_by_its_file_name_and_never_by_its_path()
    {
        OwnerProcess.Resolve(_lookup).ShouldBe("KeePass.exe");
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\notepad.exe", "notepad.exe")]
    [InlineData(@"\\server\share\tools\Tool.EXE", "Tool.EXE")]
    [InlineData("chrome.exe", "chrome.exe")]
    [InlineData("C:/Users/me/app.exe", "app.exe")]
    [InlineData(@"C:\Program Files\App\", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void Paths_of_every_shape_give_just_the_file_name(string path, string? expected)
    {
        _lookup.ImagePath = path;

        OwnerProcess.Resolve(_lookup).ShouldBe(expected);
    }

    [Fact]
    public void A_clipboard_with_no_owner_window_has_no_source()
    {
        _lookup.Window = 0;

        OwnerProcess.Resolve(_lookup).ShouldBeNull();
    }

    [Fact]
    public void A_window_that_belongs_to_no_process_has_no_source()
    {
        _lookup.ProcessId = 0;

        OwnerProcess.Resolve(_lookup).ShouldBeNull();
    }

    [Fact]
    public void A_process_whose_image_Windows_will_not_show_has_no_source_and_no_error()
    {
        // A protected or higher-privileged process refuses the query. That must never turn into an exception
        // or into a request for more rights.
        _lookup.ImagePath = null;

        OwnerProcess.Resolve(_lookup).ShouldBeNull();
    }
}
