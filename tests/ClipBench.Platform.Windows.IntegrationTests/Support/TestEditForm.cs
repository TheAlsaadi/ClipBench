using System.Drawing;
using System.Windows.Forms;

namespace ClipBench.Platform.Windows.IntegrationTests.Support;

/// <summary>
/// A real top-level window with a text box, on a thread of its own, as a paste target. Ctrl+V into it puts the
/// clipboard text in the box, so a test can see whether a paste really arrived.
/// </summary>
internal sealed class TestEditForm : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private Form? _form;
    private TextBox? _box;

    private TestEditForm()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Test edit window" };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>The window handle.</summary>
    public nint Handle { get; private set; }

    /// <summary>What the text box holds now.</summary>
    public string Text => (string)_form!.Invoke(() => _box!.Text);

    public static TestEditForm Create()
    {
        var window = new TestEditForm();
        window._thread.Start();
        window._ready.Wait(TimeSpan.FromSeconds(15)).ShouldBeTrue("the test window did not appear");
        return window;
    }

    /// <summary>Asks Windows to put this window in front. Windows protects the window the user is working in, so this can be refused.</summary>
    public void TryActivate() => _form!.Invoke(() =>
    {
        _form.Activate();
        _box!.Focus();
    });

    public void Dispose()
    {
        if (_form is { IsDisposed: false })
        {
            _form.Invoke(_form.Close);
        }

        _thread.Join(TimeSpan.FromSeconds(5));
        _ready.Dispose();
    }

    private void Run()
    {
        using var form = new Form { Text = "ClipBench paste test", Size = new Size(400, 200), StartPosition = FormStartPosition.Manual, Location = new Point(40, 40) };
        _box = new TextBox { Multiline = true, Dock = DockStyle.Fill };
        form.Controls.Add(_box);
        _form = form;
        form.Shown += (_, _) =>
        {
            Handle = form.Handle;
            _ready.Set();
        };
        Application.Run(form);
    }
}
