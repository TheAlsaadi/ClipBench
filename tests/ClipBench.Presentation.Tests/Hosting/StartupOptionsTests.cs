using ClipBench.Core.Hotkeys;
using ClipBench.Presentation.Hosting;

namespace ClipBench.Presentation.Tests.Hosting;

public class StartupOptionsTests
{
    private static readonly string _absolute = Path.Combine(Path.GetTempPath(), "clipbench-test");

    [Fact]
    public void With_no_arguments_everything_is_the_default()
    {
        var options = StartupOptions.Parse([]);

        options.DataDirectory.ShouldBeNull();
        options.Hotkey.ShouldBeNull();
        options.CollectHotkey.ShouldBeNull();
        options.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void The_collect_shortcut_can_be_given_as_a_separate_value_or_after_an_equals_sign()
    {
        StartupOptions.Parse(["--collect-hotkey", "F23"]).CollectHotkey.ShouldBe(Hotkey.Parse("F23").Value);
        StartupOptions.Parse(["--collect-hotkey=Ctrl+Alt+K"]).CollectHotkey.ShouldBe(Hotkey.Parse("Ctrl+Alt+K").Value);
    }

    [Fact]
    public void The_collect_shortcut_and_the_popup_shortcut_are_separate()
    {
        var options = StartupOptions.Parse(["--hotkey", "F24", "--collect-hotkey", "F23"]);

        options.Hotkey.ShouldBe(Hotkey.Parse("F24").Value);
        options.CollectHotkey.ShouldBe(Hotkey.Parse("F23").Value);
        options.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void A_collect_shortcut_that_does_not_parse_is_ignored_with_a_note()
    {
        var options = StartupOptions.Parse(["--collect-hotkey", "Ctrl+Nonsense"]);

        options.CollectHotkey.ShouldBeNull();
        options.Problems.ShouldBe(["--collect-hotkey is not a shortcut ClipBench understands, so it was ignored."]);
    }

    [Fact]
    public void Screen_capture_stays_blocked_unless_the_switch_is_given()
    {
        StartupOptions.Parse([]).AllowScreenCapture.ShouldBeFalse();
        StartupOptions.Parse(["--allow-screen-capture"]).AllowScreenCapture.ShouldBeTrue();
        StartupOptions.Parse(["--ALLOW-SCREEN-CAPTURE"]).AllowScreenCapture.ShouldBeTrue();
    }

    [Fact]
    public void The_capture_switch_takes_no_value_so_the_next_argument_is_read_on_its_own()
    {
        var options = StartupOptions.Parse(["--allow-screen-capture", "--hotkey", "F24"]);

        options.AllowScreenCapture.ShouldBeTrue();
        options.Hotkey.ShouldBe(Hotkey.Parse("F24").Value);
        options.Problems.ShouldBeEmpty();

        var stray = StartupOptions.Parse(["--allow-screen-capture", "stray"]);

        stray.AllowScreenCapture.ShouldBeTrue();
        stray.Problems.ShouldBe(["The argument stray is not known, so it was ignored."]);
    }

    [Fact]
    public void A_value_given_to_the_capture_switch_is_ignored_with_a_note()
    {
        var options = StartupOptions.Parse(["--allow-screen-capture=yes"]);

        options.AllowScreenCapture.ShouldBeFalse();
        options.Problems.ShouldBe(["--allow-screen-capture takes no value, so it was ignored."]);
    }

    [Fact]
    public void The_data_folder_can_be_given_as_a_separate_value_or_after_an_equals_sign()
    {
        StartupOptions.Parse(["--data-dir", _absolute]).DataDirectory.ShouldBe(_absolute);
        StartupOptions.Parse([$"--data-dir={_absolute}"]).DataDirectory.ShouldBe(_absolute);
    }

    [Fact]
    public void The_shortcut_can_be_given_as_a_separate_value_or_after_an_equals_sign()
    {
        StartupOptions.Parse(["--hotkey", "F24"]).Hotkey.ShouldBe(Hotkey.Parse("F24").Value);
        StartupOptions.Parse(["--hotkey=Ctrl+Alt+Space"]).Hotkey.ShouldBe(Hotkey.Parse("Ctrl+Alt+Space").Value);
    }

    [Fact]
    public void Both_switches_can_be_given_together_in_any_order()
    {
        var options = StartupOptions.Parse(["--hotkey=F24", "--data-dir", _absolute]);

        options.Hotkey.ShouldBe(Hotkey.Parse("F24").Value);
        options.DataDirectory.ShouldBe(_absolute);
        options.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void Switch_names_are_not_case_sensitive()
    {
        StartupOptions.Parse(["--DATA-DIR", _absolute]).DataDirectory.ShouldBe(_absolute);
        StartupOptions.Parse(["--Hotkey", "F24"]).Hotkey.ShouldNotBeNull();
    }

    [Fact]
    public void A_folder_that_is_not_absolute_is_ignored_with_a_note()
    {
        var options = StartupOptions.Parse(["--data-dir", @"relative\folder"]);

        options.DataDirectory.ShouldBeNull();
        options.Problems.ShouldBe(["--data-dir needs a full path, so it was ignored."]);
    }

    [Theory]
    [InlineData(@"\\server\share\clips")]
    [InlineData(@"\\?\C:\clips")]
    public void A_network_or_device_path_is_ignored_with_a_note_because_the_app_makes_no_network_calls(string path)
    {
        var options = StartupOptions.Parse(["--data-dir", path]);

        options.DataDirectory.ShouldBeNull();
        options.Problems.ShouldBe(["--data-dir needs a folder on this computer, so it was ignored."]);
    }

    [Fact]
    public void A_shortcut_that_does_not_parse_is_ignored_with_a_note()
    {
        var options = StartupOptions.Parse(["--hotkey", "Ctrl+Nonsense"]);

        options.Hotkey.ShouldBeNull();
        options.Problems.ShouldBe(["--hotkey is not a shortcut ClipBench understands, so it was ignored."]);
    }

    [Theory]
    [InlineData("--data-dir")]
    [InlineData("--hotkey")]
    [InlineData("--data-dir=")]
    [InlineData("--hotkey=")]
    [InlineData("--collect-hotkey")]
    [InlineData("--collect-hotkey=")]
    public void A_switch_without_a_value_is_ignored_with_a_note(string argument)
    {
        var options = StartupOptions.Parse([argument]);

        options.DataDirectory.ShouldBeNull();
        options.Hotkey.ShouldBeNull();
        options.Problems.Length.ShouldBe(1);
        options.Problems[0].ShouldStartWith(argument.TrimEnd('=') + " needs a value");
    }

    [Fact]
    public void An_unknown_argument_is_ignored_with_a_note_and_does_not_stop_the_others()
    {
        var options = StartupOptions.Parse(["--surprise", "--hotkey", "F24", "stray"]);

        options.Hotkey.ShouldBe(Hotkey.Parse("F24").Value);
        options.Problems.ShouldBe(["The argument --surprise is not known, so it was ignored.", "The argument stray is not known, so it was ignored."]);
    }

    [Fact]
    public void A_switch_given_twice_uses_the_last_value()
    {
        var options = StartupOptions.Parse(["--hotkey", "F23", "--hotkey", "F24"]);

        options.Hotkey.ShouldBe(Hotkey.Parse("F24").Value);
    }

    [Fact]
    public void A_value_that_looks_like_the_next_switch_is_not_taken_as_the_value()
    {
        var options = StartupOptions.Parse(["--data-dir", "--hotkey", "F24"]);

        options.DataDirectory.ShouldBeNull();
        options.Hotkey.ShouldBe(Hotkey.Parse("F24").Value);
        options.Problems.ShouldBe(["--data-dir needs a value, so it was ignored."]);
    }
}
