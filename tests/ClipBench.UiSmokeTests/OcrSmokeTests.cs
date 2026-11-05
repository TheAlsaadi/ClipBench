using System.Drawing;
using System.Windows.Forms;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Windows.Media.Ocr;

namespace ClipBench.UiSmokeTests;

/// <summary>
/// Copying text from the screen, end to end: the shortcut, the overlay, a real drag over a window that shows known text,
/// the text recognizer of Windows, the clipboard and the history. Like the other smoke tests they need a desktop nobody is
/// using, and they save the user's clipboard first and put it back. The overlay is cancelled with a right click, never with
/// Esc, so no key can reach the window the person running the tests is working in. The shortcut is F22 here.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SharedDesktop.Name)]
public sealed class OcrSmokeTests(AppUnderTest app) : IClassFixture<AppUnderTest>
{
    private static readonly Rectangle _where = new(220, 220, 900, 240);

    [Fact]
    public void A_drag_over_text_on_the_screen_copies_it_to_the_clipboard_and_the_history()
    {
        SkipWithoutRecognizer();
        using var window = ScreenTextWindow.Show("ClipBench OCR 2026", _where);
        var saved = SavedClipboard.Take();
        try
        {
            ShowOverlay();

            // The whole window, a little inside its edges.
            AppUnderTest.DragBy(new Point(_where.X + 6, _where.Y + 6), _where.Width - 12, _where.Height - 12);

            AppUnderTest.WaitFor(() => !app.OverlayIsShown(), 10_000).ShouldBeTrue("the overlay did not close after the drag");
            AppUnderTest.WaitFor(() => SavedClipboard.CurrentText().Contains("2026", StringComparison.Ordinal), 20_000)
                .ShouldBeTrue($"the clipboard holds: \"{SavedClipboard.CurrentText()}\"");
            var copied = SavedClipboard.CurrentText();
            copied.ShouldContain("OCR", Case.Insensitive);
            copied.ShouldContain("ClipBench", Case.Insensitive);

            var popup = app.OpenPopup();
            AppUnderTest.WaitFor(() => AppUnderTest.RowNames(popup).Any(name => name.Contains("2026", StringComparison.Ordinal)), 10_000)
                .ShouldBeTrue("the text was not in the history: " + string.Join(" | ", AppUnderTest.RowNames(popup)));
            app.ClosePopup();
        }
        finally
        {
            saved.Restore();
        }
    }

    [Fact]
    public void A_right_click_closes_the_overlay_and_copies_nothing()
    {
        SkipWithoutRecognizer();
        using var window = ScreenTextWindow.Show("ClipBench OCR cancel", _where);
        var saved = SavedClipboard.Take();
        try
        {
            var before = SavedClipboard.CurrentText();
            ShowOverlay();

            AppUnderTest.PointAt(new Point(_where.X + 100, _where.Y + 100));
            Mouse.RightClick();

            AppUnderTest.WaitFor(() => !app.OverlayIsShown(), 10_000).ShouldBeTrue("the right click did not close the overlay");
            SavedClipboard.CurrentText().ShouldBe(before);
        }
        finally
        {
            saved.Restore();
        }
    }

    [Fact]
    public void A_drag_over_an_empty_area_says_no_text_was_found_and_copies_nothing()
    {
        SkipWithoutRecognizer();
        using var window = ScreenTextWindow.Show(string.Empty, _where);
        var saved = SavedClipboard.Take();
        try
        {
            var before = SavedClipboard.CurrentText();
            ShowOverlay();

            AppUnderTest.DragBy(new Point(_where.X + 30, _where.Y + 30), 300, 120);

            AppUnderTest.WaitFor(() => !app.OverlayIsShown(), 10_000).ShouldBeTrue("the overlay did not close after the drag");
            AppUnderTest.WaitFor(() => app.ReadLog().Contains("ScreenTextFailed", StringComparison.Ordinal), 15_000).ShouldBeTrue("the failure was not logged");
            SavedClipboard.CurrentText().ShouldBe(before);
        }
        finally
        {
            saved.Restore();
        }
    }

    private static void SkipWithoutRecognizer()
    {
        if (OcrEngine.TryCreateFromUserProfileLanguages() is null)
        {
            Assert.Skip("Windows has no text-recognition language installed for this user, so the text cannot be read.");
        }
    }

    private void ShowOverlay()
    {
        Keyboard.Type(VirtualKeyShort.F22);
        AppUnderTest.WaitFor(app.OverlayIsShown, 10_000).ShouldBeTrue("the shortcut did not show the overlay. " + app.Describe());
    }

    /// <summary>A plain white top-most window that shows one line of large black text and does not take the focus.</summary>
    private sealed class ScreenTextWindow : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _shown = new();
        private Form? _form;

        private ScreenTextWindow(string text, Rectangle bounds)
        {
            _thread = new Thread(() =>
            {
                _form = new NoFocusForm { BackColor = Color.White, FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = bounds, TopMost = true, ShowInTaskbar = false };
                _form.Controls.Add(new System.Windows.Forms.Label
                {
                    Text = text,
                    Font = new Font("Arial", 48, FontStyle.Regular, GraphicsUnit.Pixel),
                    ForeColor = Color.Black,
                    BackColor = Color.White,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                });
                _form.Shown += (_, _) => _shown.Set();
                Application.Run(_form);
            })
            { IsBackground = true, Name = "Screen text window" };
            _thread.SetApartmentState(ApartmentState.STA);
        }

        public static ScreenTextWindow Show(string text, Rectangle bounds)
        {
            var window = new ScreenTextWindow(text, bounds);
            window._thread.Start();
            window._shown.Wait(TimeSpan.FromSeconds(15)).ShouldBeTrue("the test window did not show");
            Thread.Sleep(500);   // the desktop compositor needs a moment to put the new window on screen
            return window;
        }

        public void Dispose()
        {
            _form?.Invoke(_form.Close);
            _thread.Join(TimeSpan.FromSeconds(5));
            _shown.Dispose();
        }

        private sealed class NoFocusForm : Form
        {
            protected override bool ShowWithoutActivation => true;
        }
    }
}
