using System.Diagnostics;
using System.Text;
using ClipBench.Core.Clips;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.IntegrationTests.Support;
using ClipBench.Platform.Windows.Pasting;
using ClipBench.Platform.Windows.Windowing;

namespace ClipBench.Platform.Windows.IntegrationTests.Pasting;

/// <summary>
/// Pasting and window inspection on the real operating system. The paste target is the test's own window, and
/// the injector sends no key unless that window has focus, so nothing is typed into the user's programs.
/// Windows protects the window the user is working in, so the test window may not be allowed to take focus:
/// the paste tests then skip, saying why.
/// </summary>
[Collection("Clipboard")]
[Trait("Category", "Integration")]
public sealed class RealPasteTests(RealClipboardFixture clipboard)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string ThisProgram => Path.GetFileName(Environment.ProcessPath)!;

    private PasteInjector RealInjector()
    {
        var window = new Win32WindowApi();
        var tracker = new ForegroundTracker(window, clipboard.Api, new Win32ProcessIntegrity());
        return new PasteInjector(new ClipboardWriter(clipboard.Api, TimeProvider.System), clipboard.Api, tracker, window, new Win32KeyboardApi(), TimeProvider.System);
    }

    private async Task PutAsync(string text)
    {
        using var session = await ClipboardOpener.OpenAsync(clipboard.ForeignApi, ClipboardRetryPolicy.Default, TimeProvider.System, Ct);
        session.ShouldNotBeNull();
        session.Empty().ShouldBeTrue();
        session.Set(ClipboardFormats.UnicodeText, Encoding.Unicode.GetBytes(text + "\0")).ShouldBeTrue();
    }

    private async Task<string?> ClipboardTextAsync()
    {
        using var session = await ClipboardOpener.OpenAsync(clipboard.ForeignApi, ClipboardRetryPolicy.Default, TimeProvider.System, Ct);
        var bytes = session?.Read(ClipboardFormats.UnicodeText, 1 << 20);
        return bytes is null ? null : Encoding.Unicode.GetString(bytes).TrimEnd('\0');
    }

    // --- window inspection ---

    [Fact]
    public void A_window_of_this_program_is_described_with_this_program_and_no_blocking()
    {
        var tracker = new ForegroundTracker(new Win32WindowApi(), clipboard.Api, new Win32ProcessIntegrity());

        var result = tracker.Describe(new WindowHandle(clipboard.Window.Handle));

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.ProcessId.ShouldBe(Environment.ProcessId);
        result.Value.ProcessName.ShouldBe(ThisProgram);
        result.Value.BlocksInputFromUs.ShouldBeFalse();
    }

    [Fact]
    public void Whatever_window_has_focus_can_be_recorded_without_error_or_elevation()
    {
        var tracker = new ForegroundTracker(new Win32WindowApi(), clipboard.Api, new Win32ProcessIntegrity());

        var result = tracker.Capture();

        // When nobody has focus (a locked screen) the answer is "not found"; otherwise it is a real window.
        if (result.IsFailure)
        {
            result.Error.Code.ShouldBe(ErrorCode.TargetNotFound);
        }
        else
        {
            result.Value.Window.IsNone.ShouldBeFalse();
            result.Value.ProcessId.ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public void The_integrity_of_this_program_reads_back_the_same_for_the_pid_and_the_pseudo_handle()
    {
        var integrity = new Win32ProcessIntegrity();

        var own = integrity.Query((uint)Environment.ProcessId);

        own.Outcome.ShouldBe(IntegrityOutcome.Known);
        own.Level.ShouldBe(integrity.OwnLevel);
        integrity.OwnLevel.ShouldBeOneOf(0x2000, 0x3000);   // an ordinary user, or an administrator who elevated
    }

    [Fact]
    public void A_process_of_the_same_user_is_known_and_the_system_process_is_not_readable_or_higher()
    {
        var integrity = new Win32ProcessIntegrity();
        var explorer = Process.GetProcessesByName("explorer").FirstOrDefault();
        explorer.ShouldNotBeNull("the desktop shell runs in every interactive session");

        var shell = integrity.Query((uint)explorer.Id);
        var system = integrity.Query(4);   // "System", the kernel's own process

        shell.Outcome.ShouldBe(IntegrityOutcome.Known);
        shell.Level.ShouldBeLessThanOrEqualTo(integrity.OwnLevel);
        (system.Outcome == IntegrityOutcome.AccessDenied || system.Level > integrity.OwnLevel).ShouldBeTrue("keystrokes to the system process would be blocked");
    }

    [Fact]
    public void A_process_number_that_does_not_exist_is_not_found()
    {
        new Win32ProcessIntegrity().Query(0xFFFFFFF0).Outcome.ShouldBe(IntegrityOutcome.NotFound);
    }

    // --- pasting ---

    [Fact]
    public async Task A_paste_reaches_a_real_window_and_the_users_clipboard_comes_back()
    {
        using var form = TestEditForm.Create();
        await PutAsync("what the user had copied");
        form.TryActivate();

        var result = await RealInjector().PasteAsync(
            new PasteRequest { Content = new TextContent("pasted by ClipBench"), Target = new WindowHandle(form.Handle), RestoreDelay = TimeSpan.FromMilliseconds(400) },
            Ct);

        if (result.IsFailure && result.Error.Code == ErrorCode.FocusNotRestored)
        {
            Assert.Skip("Windows would not let the test window take focus (it protects the window you are working in), so no key was sent. Run this test from a terminal that is in front.");
        }

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? $"{result.Error.Code}: {result.Error.Message}" : string.Empty);
        SpinWait.SpinUntil(() => form.Text == "pasted by ClipBench", TimeSpan.FromSeconds(10)).ShouldBeTrue($"the window holds: \"{form.Text}\"");
        (await ClipboardTextAsync()).ShouldBe("what the user had copied", "the clipboard was put back");
    }

    [Fact]
    public async Task A_paste_to_a_window_that_is_gone_is_reported_and_leaves_the_clipboard_alone()
    {
        await PutAsync("untouched");
        IntPtr gone;
        using (var form = TestEditForm.Create())
        {
            gone = form.Handle;
        }

        var result = await RealInjector().PasteAsync(new PasteRequest { Content = new TextContent("never pasted"), Target = new WindowHandle(gone) }, Ct);

        result.Error.Code.ShouldBe(ErrorCode.TargetNotFound);
        (await ClipboardTextAsync()).ShouldBe("untouched");
    }
}
