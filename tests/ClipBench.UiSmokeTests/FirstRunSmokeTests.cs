using FlaUI.Core.AutomationElements;

namespace ClipBench.UiSmokeTests;

/// <summary>
/// A copy of the app that starts fresh, with the first-run question not yet answered, so the Settings window opens by itself.
/// Only "No thanks" is pressed: "Yes" would put a start-up entry for the test build into the real registry of the person
/// running the tests. The entry is covered by tests against a throwaway key.
/// </summary>
public sealed class FirstRunApp : AppUnderTest
{
    /// <inheritdoc/>
    protected override bool FirstRunAnswered => false;
}

[Trait("Category", "Integration")]
[Collection(SharedDesktop.Name)]
public sealed class FirstRunSmokeTests(FirstRunApp app) : IClassFixture<FirstRunApp>
{
    [Fact]
    public void The_first_run_opens_the_settings_with_the_question_and_saying_no_ends_it_without_a_startup_entry()
    {
        Window? settings = null;
        AppUnderTest.WaitFor(() => (settings = app.FindWindow("ClipBench settings")) is not null, 30_000).ShouldBeTrue("the settings window did not open by itself");
        AppUnderTest.WaitFor(() => settings!.FindFirstDescendant(cf => cf.ByAutomationId("FirstRunNo")) is not null, 10_000).ShouldBeTrue("the question did not show");
        AppUnderTest.SaveScreenshot(settings!, "settings-first-run");

        settings!.FindFirstDescendant(cf => cf.ByAutomationId("FirstRunNo"))!.Patterns.Invoke.Pattern.Invoke();

        AppUnderTest.WaitFor(() => settings.FindFirstDescendant(cf => cf.ByAutomationId("FirstRunNo")) is null, 10_000).ShouldBeTrue("the question did not go away");
        AppUnderTest.WaitFor(() => File.Exists(app.SettingsPath) && File.ReadAllText(app.SettingsPath).Contains("\"firstRunCompleted\": true", StringComparison.Ordinal), 10_000)
            .ShouldBeTrue("the answer was not saved");
        File.ReadAllText(app.SettingsPath).ShouldContain("\"startWithWindows\": false");
        settings.Close();
    }
}
