using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace ClipBench.UiSmokeTests;

/// <summary>
/// Batch, table, queue and collect paste, driven through the real app with the real keyboard into a real window. Like
/// <see cref="SmokeTests"/>, they need a desktop nobody is using and run on demand. Clips are marked by searching for
/// them, so the tests do not depend on the order they run in.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SharedDesktop.Name)]
public sealed class MultiPasteSmokeTests(AppUnderTest app) : IClassFixture<AppUnderTest>
{
    private const string Invoice = "Invoice 2026-10 for ACME Corp";
    private const string Meeting = "Meeting notes: budget review and hiring plan";
    private const string Link = "https://learn.microsoft.com/windows/apps/winui/";

    [Fact]
    public void Enter_with_marked_clips_pastes_them_joined_by_a_line_break_in_the_order_they_were_marked()
    {
        using var target = ActiveTarget();
        var popup = app.OpenPopup();
        Mark(popup, "meeting", "invoice");
        AppUnderTest.SaveScreenshot(popup, "popup-two-marked");

        Keyboard.Type(VirtualKeyShort.ENTER);

        AppUnderTest.WaitFor(() => app.FindPopup() is null, 10_000).ShouldBeTrue("the popup did not close");
        AppUnderTest.WaitFor(() => target.Text == Meeting + "\r\n" + Invoice, 15_000).ShouldBeTrue($"the window holds: \"{target.Text}\"");
    }

    [Fact]
    public void Control_T_pastes_the_marked_clips_as_a_column_and_control_shift_T_as_a_row()
    {
        using var target = ActiveTarget();
        var popup = app.OpenPopup();
        Mark(popup, "invoice", "meeting");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_T);

        AppUnderTest.WaitFor(() => target.Text == Invoice + "\r\n" + Meeting, 15_000).ShouldBeTrue($"the window holds: \"{target.Text}\"");
        target.Clear();
        target.TryActivate().ShouldBeTrue();

        popup = app.OpenPopup();
        Mark(popup, "invoice", "meeting");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_T);

        AppUnderTest.WaitFor(() => target.Text == Invoice + "\t" + Meeting, 15_000).ShouldBeTrue($"the window holds: \"{target.Text}\"");
    }

    [Fact]
    public void Queue_mode_pastes_one_marked_clip_per_control_V_in_the_order_they_were_marked()
    {
        using var target = ActiveTarget();
        var popup = app.OpenPopup();
        Mark(popup, "learn.microsoft", "invoice", "meeting");

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Q);

        AppUnderTest.WaitFor(() => app.FindPopup() is null, 10_000).ShouldBeTrue("the popup did not close");
        AppUnderTest.WaitFor(() => app.QueueText() == "Queue 1/3 · Esc to stop", 10_000).ShouldBeTrue($"the queue window says: \"{app.QueueText()}\"");
        AppUnderTest.SaveScreenshot(app.FindQueueWindow()!, "queue-window");
        target.TryActivate().ShouldBeTrue("the queue window must not keep the focus");

        string[] expected = [Link, Invoice, Meeting];
        for (var i = 0; i < expected.Length; i++)
        {
            var before = target.Text;
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
            AppUnderTest.WaitFor(() => target.Text == before + expected[i], 15_000).ShouldBeTrue($"paste {i + 1}: the window holds \"{target.Text}\". The log says: {app.ReadLog()}");
            if (i < expected.Length - 1)
            {
                AppUnderTest.WaitFor(() => app.QueueText() == $"Queue {i + 2}/3 · Esc to stop", 10_000).ShouldBeTrue($"the queue window says: \"{app.QueueText()}\"");
                Keyboard.Type(VirtualKeyShort.RETURN);
                AppUnderTest.WaitFor(() => target.Text.EndsWith("\r\n", StringComparison.Ordinal), 10_000).ShouldBeTrue("the line break did not arrive");
            }
        }

        AppUnderTest.WaitFor(() => app.FindQueueWindow() is null, 10_000).ShouldBeTrue("the queue window stayed after the last clip");
        target.Text.ShouldBe(Link + "\r\n" + Invoice + "\r\n" + Meeting);
        AppUnderTest.WaitFor(() => app.ReadLog().Contains("QueueFinished", StringComparison.Ordinal), 10_000).ShouldBeTrue();
    }

    [Fact]
    public void Esc_stops_a_queue_and_its_window_goes_away()
    {
        using var target = ActiveTarget();
        var popup = app.OpenPopup();
        Mark(popup, "invoice", "meeting");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Q);
        AppUnderTest.WaitFor(() => app.QueueText() is not null, 10_000).ShouldBeTrue("the queue window did not appear");
        target.TryActivate().ShouldBeTrue();

        Keyboard.Type(VirtualKeyShort.ESCAPE);

        AppUnderTest.WaitFor(() => app.FindQueueWindow() is null, 10_000).ShouldBeTrue("Esc did not stop the queue");
        AppUnderTest.WaitFor(() => app.ReadLog().Contains("QueueStopped", StringComparison.Ordinal), 10_000).ShouldBeTrue();
        target.Text.ShouldBeEmpty();
    }

    [Fact]
    public void The_collect_shortcut_collects_copies_into_one_clip_that_is_put_on_the_clipboard()
    {
        var saved = SavedClipboard.Take();
        try
        {
            using var target = ActiveTarget();
            var collectedBefore = Count(app.ReadLog(), "CaptureCollected");

            Keyboard.Type(VirtualKeyShort.F23);
            AppUnderTest.WaitFor(() => app.ReadLog().Contains("CollectStarted", StringComparison.Ordinal), 10_000).ShouldBeTrue("the shortcut did not start collecting");
            target.Copy("first quote");
            AppUnderTest.WaitFor(() => Count(app.ReadLog(), "CaptureCollected") == collectedBefore + 1, 10_000).ShouldBeTrue("the first copy was not collected");
            target.Copy("second quote");
            AppUnderTest.WaitFor(() => Count(app.ReadLog(), "CaptureCollected") == collectedBefore + 2, 10_000).ShouldBeTrue("the second copy was not collected");

            Keyboard.Type(VirtualKeyShort.F23);
            AppUnderTest.WaitFor(
                () => app.ReadLog().Split('\n').Any(line => line.Contains("CollectFinished", StringComparison.Ordinal) && line.Contains("count=2", StringComparison.Ordinal)),
                10_000).ShouldBeTrue("the shortcut did not finish collecting");

            target.TryActivate().ShouldBeTrue();
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
            AppUnderTest.WaitFor(() => target.Text == "first quote\r\nsecond quote", 15_000).ShouldBeTrue($"the window holds: \"{target.Text}\"");
        }
        finally
        {
            saved.Restore();
        }
    }

    private PasteTargetWindow ActiveTarget()
    {
        app.ClosePopup();
        app.EndQueue();
        var target = PasteTargetWindow.Create();
        if (!target.TryActivate())
        {
            target.Dispose();
            Assert.Skip("Windows would not let the test window take focus (it protects the window you are working in), so no key was sent. Run this test from a terminal that is in front.");
        }

        return target;
    }

    /// <summary>Finds each clip by searching for it and marks it with Ctrl+Space, then clears the search.</summary>
    private static void Mark(Window popup, params string[] searches)
    {
        foreach (var search in searches)
        {
            Keyboard.Type(search);
            AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length == 1, 10_000).ShouldBeTrue($"no single clip matches \"{search}\"");
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SPACE);
            for (var i = 0; i < search.Length; i++)
            {
                Keyboard.Type(VirtualKeyShort.BACK);
            }

            AppUnderTest.WaitFor(() => AppUnderTest.Rows(popup).Length > 1, 10_000).ShouldBeTrue("the search did not clear");
        }
    }

    private static int Count(string text, string word)
    {
        var count = 0;
        for (var at = text.IndexOf(word, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + word.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
