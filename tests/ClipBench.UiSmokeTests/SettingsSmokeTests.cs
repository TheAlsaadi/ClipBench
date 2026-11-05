using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace ClipBench.UiSmokeTests;

/// <summary>
/// The Settings window, driven through the real app. Like the other smoke tests they need a desktop nobody is using and
/// run on demand. Buttons, switches and list items are used through automation rather than clicked, and the window is
/// closed through automation too, so nothing but the key that opens the window is typed. Every test uses its own
/// names, so they do not depend on the order they run in; the last one wipes the history, so it only reads what it did.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SharedDesktop.Name)]
public sealed class SettingsSmokeTests(AppUnderTest app) : IClassFixture<AppUnderTest>
{
    [Theory]
    [InlineData("General", "GeneralPanel", "StartWithWindowsSwitch")]
    [InlineData("Shortcuts", "HotkeysPanel", "PopupHotkeyBox")]
    [InlineData("History", "HistoryPanel", "MaxCountBox")]
    [InlineData("Privacy", "PrivacyPanel", "IncognitoSwitch")]
    [InlineData("Paste", "PastePanel", "RestoreClipboardSwitch")]
    [InlineData("Snippets", "SnippetsPanel", "SnippetNames")]
    [InlineData("About", "AboutPanel", "AboutVersion")]
    public void Every_section_opens_and_shows_its_controls(string section, string panel, string control)
    {
        var settings = OpenSettings();

        Choose(settings, section);

        AppUnderTest.WaitFor(() => Find(settings, panel) is not null, 10_000).ShouldBeTrue($"the {section} section did not open");
        AppUnderTest.WaitFor(() => Find(settings, control) is not null, 10_000).ShouldBeTrue($"the {section} section is missing {control}");
        AppUnderTest.SaveScreenshot(settings, "settings-" + section.ToLowerInvariant());
        CloseSettings(settings);
    }

    [Fact]
    public void The_about_section_names_the_license_and_the_promise_not_to_use_the_network()
    {
        var settings = OpenSettings();

        Choose(settings, "About");

        AppUnderTest.WaitFor(() => Find(settings, "AboutLicense") is not null, 10_000).ShouldBeTrue("the license did not show");
        Find(settings, "AboutLicense")!.Name.ShouldContain("MIT");
        Find(settings, "AboutNoNetwork")!.Name.ShouldContain("no network");
        Find(settings, "AboutVersion")!.Name.ShouldNotBeNullOrWhiteSpace();
        CloseSettings(settings);
    }

    [Fact]
    public void The_default_excluded_programs_are_listed_and_a_new_one_can_be_added_and_removed()
    {
        var settings = OpenSettings();
        Choose(settings, "Privacy");
        AppUnderTest.WaitFor(() => Find(settings, "ExcludedPrograms") is not null, 10_000).ShouldBeTrue("the list did not show");

        AppUnderTest.WaitFor(() => ListedPrograms(settings).Contains("KeePassXC"), 10_000).ShouldBeTrue($"the list holds: {string.Join(", ", ListedPrograms(settings))}");

        Find(settings, "NewProgramBox")!.AsTextBox().Enter("smoke-tool.exe");
        Find(settings, "AddProgramButton")!.Patterns.Invoke.Pattern.Invoke();
        AppUnderTest.WaitFor(() => ListedPrograms(settings).Contains("smoke-tool"), 10_000).ShouldBeTrue($"the program was not added: {string.Join(", ", ListedPrograms(settings))}");

        var row = Find(settings, "ExcludedPrograms")!.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .First(item => item.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Any(text => text.Name == "smoke-tool"));
        row.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Remove")))!.Patterns.Invoke.Pattern.Invoke();
        AppUnderTest.WaitFor(() => !ListedPrograms(settings).Contains("smoke-tool"), 10_000).ShouldBeTrue("the program was not removed");
        CloseSettings(settings);
    }

    [Fact]
    public void A_name_that_cannot_be_a_program_is_refused_with_a_reason()
    {
        var settings = OpenSettings();
        Choose(settings, "Privacy");
        AppUnderTest.WaitFor(() => Find(settings, "NewProgramBox") is not null, 10_000).ShouldBeTrue("the box did not show");

        Find(settings, "NewProgramBox")!.AsTextBox().Enter("a*b");
        Find(settings, "AddProgramButton")!.Patterns.Invoke.Pattern.Invoke();

        AppUnderTest.WaitFor(() => !string.IsNullOrWhiteSpace(Find(settings, "PrivacyError")?.Name), 10_000).ShouldBeTrue("no reason was shown");
        CloseSettings(settings);
    }

    [Fact]
    public void The_incognito_switch_turns_on_and_off()
    {
        var settings = OpenSettings();
        Choose(settings, "Privacy");
        AppUnderTest.WaitFor(() => Find(settings, "IncognitoSwitch") is not null, 10_000).ShouldBeTrue("the switch did not show");
        var incognito = Find(settings, "IncognitoSwitch")!.Patterns.Toggle.Pattern;

        incognito.Toggle();
        AppUnderTest.WaitFor(() => incognito.ToggleState.Value == ToggleState.On, 10_000).ShouldBeTrue("incognito did not turn on");
        incognito.Toggle();
        AppUnderTest.WaitFor(() => incognito.ToggleState.Value == ToggleState.Off, 10_000).ShouldBeTrue("incognito did not turn off");
        CloseSettings(settings);
    }

    [Fact]
    public void Deleting_everything_asks_first_keeps_the_clips_when_cancelled_and_wipes_them_when_confirmed()
    {
        var settings = OpenSettings();
        Choose(settings, "Privacy");
        AppUnderTest.WaitFor(() => Find(settings, "WipeButton") is not null, 10_000).ShouldBeTrue("the button did not show");

        // Asking and then keeping the clips changes nothing.
        Find(settings, "WipeButton")!.Patterns.Invoke.Pattern.Invoke();
        AppUnderTest.WaitFor(() => Find(settings, "WipeConfirmButton") is not null, 10_000).ShouldBeTrue("the question did not show");
        Find(settings, "WipeCancelButton")!.Patterns.Invoke.Pattern.Invoke();
        AppUnderTest.WaitFor(() => Find(settings, "WipeConfirmButton") is null, 10_000).ShouldBeTrue("the question did not go away");
        Find(settings, "WipeStatus")!.Name.ShouldBeNullOrEmpty();
        CloseSettings(settings);
        var popup = app.OpenPopup();
        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length > 0, 10_000).ShouldBeTrue("the clips were gone after the wipe was cancelled");
        app.ClosePopup();

        // Confirming deletes everything.
        settings = OpenSettings();
        Choose(settings, "Privacy");
        AppUnderTest.WaitFor(() => Find(settings, "WipeButton") is not null, 10_000).ShouldBeTrue("the button did not show");
        Find(settings, "WipeButton")!.Patterns.Invoke.Pattern.Invoke();
        AppUnderTest.WaitFor(() => Find(settings, "WipeConfirmButton") is not null, 10_000).ShouldBeTrue("the question did not show");
        Find(settings, "WipeConfirmButton")!.Patterns.Invoke.Pattern.Invoke();
        AppUnderTest.WaitFor(() => Find(settings, "WipeStatus")?.Name?.Contains("Everything was deleted", StringComparison.Ordinal) == true, 15_000)
            .ShouldBeTrue($"the wipe did not report success: \"{Find(settings, "WipeStatus")?.Name}\"");
        CloseSettings(settings);

        popup = app.OpenPopup();
        AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length == 0, 10_000).ShouldBeTrue("clips were still listed after the wipe");
        app.ClosePopup();
    }

    // Opens the popup with the real shortcut and the settings with Ctrl+E, like a person would.
    private Window OpenSettings()
    {
        _ = app.OpenPopup();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_E);
        Window? settings = null;
        AppUnderTest.WaitFor(() => (settings = app.FindWindow("ClipBench settings")) is not null, 15_000).ShouldBeTrue("the settings window did not open");
        return settings!;
    }

    // Closing through automation hides the window; no key is sent to whatever has the focus.
    private void CloseSettings(Window settings)
    {
        settings.Close();
        AppUnderTest.WaitFor(() => app.FindWindow("ClipBench settings") is null, 10_000).ShouldBeTrue("the settings window did not hide");
    }

    private static AutomationElement? Find(Window window, string automationId) =>
        window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));

    private static void Choose(Window settings, string section)
    {
        AppUnderTest.WaitFor(() => Find(settings, "SettingsSections") is not null, 10_000).ShouldBeTrue("the list of sections did not show");
        var item = Find(settings, "SettingsSections")!.FindAllChildren().First(entry => entry.Name == section);
        item.Patterns.SelectionItem.Pattern.Select();
    }

    private static string[] ListedPrograms(Window settings) =>
        [.. Find(settings, "ExcludedPrograms")!.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Select(text => text.Name)];
}
