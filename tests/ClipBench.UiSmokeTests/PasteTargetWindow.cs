using System.Drawing;
using System.Windows.Forms;

namespace ClipBench.UiSmokeTests;

/// <summary>A real top-level window with a text box, on a thread of its own, standing in for the program the user pastes into.</summary>
internal sealed class PasteTargetWindow : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private Form? _form;
    private TextBox? _box;

    private PasteTargetWindow()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Paste target window" };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public string Text => (string)_form!.Invoke(() => _box!.Text);

    /// <summary>Empties the text box.</summary>
    public void Clear() => _form!.Invoke(() => _box!.Clear());

    /// <summary>Copies text the way a program does when the user presses Ctrl+C in it: the text box puts it on the clipboard.</summary>
    /// <param name="text">The text to copy.</param>
    public void Copy(string text) => _form!.Invoke(() =>
    {
        _box!.Text = text;
        _box.SelectAll();
        _box.Copy();
        _box.Clear();
    });

    public static PasteTargetWindow Create()
    {
        var window = new PasteTargetWindow();
        window._thread.Start();
        window._ready.Wait(TimeSpan.FromSeconds(15)).ShouldBeTrue("the paste target window did not appear");
        return window;
    }

    /// <summary>Asks Windows to put the window in front. Windows protects the window the user is working in, so this can be refused.</summary>
    /// <returns>True when the window is now the one in front, the one that gets the keys.</returns>
    public bool TryActivate() => (bool)_form!.Invoke(() =>
    {
        _form.Activate();
        _ = _box!.Focus();

        // Form.ActiveForm only says the window is active on its own thread, which it can be while another program is in front.
        return Form.ActiveForm == _form && GetForegroundWindow() == _form.Handle;
    });

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern nint GetForegroundWindow();

    public void Dispose()
    {
        if (_form is { IsDisposed: false })
        {
            _form.Invoke(_form.Close);
        }

        _ = _thread.Join(TimeSpan.FromSeconds(5));
        _ready.Dispose();
    }

    private void Run()
    {
        using var form = new Form { Text = "ClipBench smoke paste target", Size = new Size(420, 160), StartPosition = FormStartPosition.Manual, Location = new Point(40, 40) };
        _box = new TextBox { Multiline = true, Dock = DockStyle.Fill };
        form.Controls.Add(_box);
        _form = form;
        form.Shown += (_, _) => _ready.Set();
        Application.Run(form);
    }
}
