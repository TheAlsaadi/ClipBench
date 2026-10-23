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

    /// <summary>True when the client area is as large as the whole window, which a window with a frame never has.</summary>
    public bool ClientFillsWindow => (bool)_form!.Invoke(() => _form.ClientSize == _form.Size);

    /// <summary>What the window answers when asked whether a click should make it the window with focus (WM_MOUSEACTIVATE).</summary>
    public nint AnswerToMouseActivate() => (nint)_form!.Invoke(() =>
        SendMessage(_form.Handle, 0x0021, 0, 0));

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    /// <summary>True when the window has the extended style that keeps it from becoming the window with focus when it is clicked.</summary>
    public bool HasNoActivateStyle => (bool)_form!.Invoke(() => (GetWindowLongPtr(_form.Handle, -20) & 0x08000000) != 0);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern nint GetWindowLongPtr(nint window, int index);

    /// <summary>The title of the window that is in front, for the message of a test that fails.</summary>
    public static string ForegroundTitle()
    {
        var window = GetForegroundWindow();
        var text = new char[256];
        var length = GetWindowText(window, text, text.Length);
        return "0x" + window.ToString("X", System.Globalization.CultureInfo.InvariantCulture) + " \"" + new string(text, 0, Math.Max(0, length)) + "\"";
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern int GetWindowText(nint window, char[] text, int count);

    /// <summary>Waits until Windows says this window is the one in front.</summary>
    public bool WaitUntilInFront(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if ((bool)_form!.Invoke(() => GetForegroundWindow() == _form.Handle))
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return false;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern nint GetForegroundWindow();

    /// <summary>Runs something on the thread that owns the window, which some calls on a window must be made from.</summary>
    /// <typeparam name="T">What it returns.</typeparam>
    /// <param name="work">What to run.</param>
    /// <returns>What it returned.</returns>
    public T OnWindowThread<T>(Func<T> work) => (T)_form!.Invoke(work);

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
