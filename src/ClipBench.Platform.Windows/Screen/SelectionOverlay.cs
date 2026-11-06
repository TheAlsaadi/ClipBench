using System.Collections.Immutable;
using System.Runtime.InteropServices;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Core.Screen;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ClipBench.Platform.Windows.Screen;

/// <summary>
/// The overlay on which the user drags a rectangle over the text to copy. One dimmed window covers each monitor, and the drag
/// stays on the monitor where it starts. The windows are layered Win32 windows drawn with GDI, because they have to be
/// see-through: the user must read the text underneath. They are hidden from screen capture, so they never end up in the
/// pixels that are read. Esc, a right click and a cancelled token close them. Nothing here reads the screen.
/// </summary>
public sealed unsafe class SelectionOverlay : IRegionPicker
{
    private const uint WmSetCursor = 0x0020;
    private const uint WmEraseBackground = 0x0014;
    private const uint WmKeyDown = 0x0100;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLeftButtonDown = 0x0201;
    private const uint WmLeftButtonUp = 0x0202;
    private const uint WmRightButtonDown = 0x0204;
    private const uint WmCaptureChanged = 0x0215;
    private const uint WmCancel = PInvoke.WM_APP + 2;
    private const int VirtualKeyEscape = 0x1B;
    private const int BorderUnits = 2;

    private readonly IMonitorLayout _monitors;
    private int _active;

    /// <summary>Creates the overlay.</summary>
    /// <param name="monitors">Where the monitors are.</param>
    public SelectionOverlay(IMonitorLayout monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        _monitors = monitors;
    }

    /// <inheritdoc/>
    public Task<Result<PixelRect>> PickAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            return Task.FromResult(Result.Failure<PixelRect>(new ResultError(ErrorCode.ScreenTextBusy, "Text is already being selected on the screen.")));
        }

        var monitors = _monitors.GetMonitors();
        if (monitors.IsDefaultOrEmpty)
        {
            Volatile.Write(ref _active, 0);
            return Task.FromResult(Result.Failure<PixelRect>(new ResultError(ErrorCode.ScreenCaptureFailed, "ClipBench could not find your monitors to show the selection on.")));
        }

        var finished = new TaskCompletionSource<Result<PixelRect>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                finished.TrySetResult(new Session(monitors).Run(cancellationToken));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                finished.TrySetResult(Result.Failure<PixelRect>(new ResultError(ErrorCode.ScreenCaptureFailed, "Windows would not show the selection on the screen.")));
            }
            finally
            {
                Volatile.Write(ref _active, 0);
            }
        })
        {
            IsBackground = true,
            Name = "ClipBench selection overlay",
        };
        try
        {
            thread.Start();
        }
        catch (Exception exception) when (exception is OutOfMemoryException or ThreadStartException)
        {
            // No thread, no overlay: the flag must not stay set, or no later selection could start.
            Volatile.Write(ref _active, 0);
            return Task.FromResult(Result.Failure<PixelRect>(new ResultError(ErrorCode.ScreenCaptureFailed, "Windows would not show the selection on the screen.")));
        }

        return finished.Task;
    }

    // One window per monitor, one message loop, one result. Everything here runs on the overlay's own thread.
    private sealed class Session
    {
        private readonly ImmutableArray<MonitorBounds> _monitors;
        private readonly List<OverlayWindow> _windows = [];
        private readonly WNDPROC _procedure;
        private readonly string _className = "ClipBench.SelectionOverlay." + Guid.NewGuid().ToString("N");
        private OverlayWindow? _dragging;
        private PixelPoint _start;
        private Result<PixelRect>? _result;

        public Session(ImmutableArray<MonitorBounds> monitors)
        {
            _monitors = monitors;
            _procedure = WindowProcedure;
        }

        public Result<PixelRect> Run(CancellationToken cancellationToken)
        {
            var instance = new HINSTANCE(PInvoke.GetModuleHandle((PCWSTR)null).Value);
            var registered = false;
            try
            {
                fixed (char* className = _className)
                {
                    var windowClass = new WNDCLASSEXW
                    {
                        cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),   // not sizeof: the struct holds a delegate
                        lpfnWndProc = _procedure,
                        hInstance = instance,
                        lpszClassName = className,
                    };
                    registered = PInvoke.RegisterClassEx(in windowClass) != 0;
                }

                if (!registered)
                {
                    return Failed();
                }

                foreach (var monitor in _monitors)
                {
                    var window = OverlayWindow.Create(_className, instance, monitor);
                    if (window is null)
                    {
                        return Failed();
                    }

                    _windows.Add(window);
                }

                foreach (var window in _windows)
                {
                    window.Show();
                }

                // The window under the pointer takes the keyboard, so Esc works at once.
                var cursor = PInvoke.GetCursorPos(out var point) ? new PixelPoint(point.X, point.Y) : default;
                var under = RegionMath.MonitorAt(_monitors, cursor);
                var first = _windows.FirstOrDefault(window => under is { } monitor && window.Monitor == monitor) ?? _windows[0];

                // When Windows refuses to hand the keyboard over (it protects the program in front), the stronger call is made, so Esc works.
                if (!PInvoke.SetForegroundWindow(first.Handle))
                {
                    PInvoke.SwitchToThisWindow(first.Handle, true);
                }

                using var registration = cancellationToken.Register(() =>
                {
                    foreach (var window in _windows)
                    {
                        _ = PInvoke.PostMessage(window.Handle, WmCancel, default, default);
                    }
                });

                // GetMessage returns -1 on error, which would read as "true" if treated as a flag.
                while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0).Value > 0)
                {
                    PInvoke.DispatchMessage(in message);
                }

                return _result ?? Cancelled();
            }
            finally
            {
                foreach (var window in _windows)
                {
                    window.Dispose();
                }

                if (registered)
                {
                    fixed (char* className = _className)
                    {
                        PInvoke.UnregisterClass(className, instance);
                    }
                }
            }
        }

        private static Result<PixelRect> Cancelled() =>
            Result.Failure<PixelRect>(new ResultError(ErrorCode.SelectionCancelled, "The selection was cancelled."));

        private static Result<PixelRect> Failed() =>
            Result.Failure<PixelRect>(new ResultError(ErrorCode.ScreenCaptureFailed, "Windows would not show the selection on the screen."));

        private void Finish(Result<PixelRect> result)
        {
            _result ??= result;
            _dragging = null;
            _ = PInvoke.ReleaseCapture();
            PInvoke.PostQuitMessage(0);
        }

        private LRESULT WindowProcedure(HWND handle, uint message, WPARAM wParam, LPARAM lParam)
        {
            try
            {
                var window = _windows.FirstOrDefault(candidate => candidate.Handle == handle);
                switch (message)
                {
                    case WmEraseBackground:
                        return new LRESULT(1);
                    case WmSetCursor:
                        _ = PInvoke.SetCursor(PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_CROSS));
                        return new LRESULT(1);
                    case WmLeftButtonDown when window is not null && _dragging is null:
                        _dragging = window;
                        _start = Cursor();
                        _ = PInvoke.SetCapture(handle);
                        window.Redraw(new PixelRect(_start.X, _start.Y, 0, 0));
                        return new LRESULT(0);
                    case WmMouseMove when _dragging is { } active:
                        active.Redraw(Between(_start, Cursor()));
                        return new LRESULT(0);
                    case WmLeftButtonUp when _dragging is { } active:
                        Finish(RegionMath.FromDrag(_start, Cursor(), active.Monitor.Bounds));
                        return new LRESULT(0);
                    case WmCaptureChanged when _dragging is not null:
                    // Something took the mouse in the middle of a drag (Alt+Tab, another window): no button-up will come, so the selection ends.
                    case WmRightButtonDown:
                    case WmCancel:
                    case PInvoke.WM_CLOSE:
                        Finish(Cancelled());
                        return new LRESULT(0);
                    case WmKeyDown when (int)wParam.Value == VirtualKeyEscape:
                        Finish(Cancelled());
                        return new LRESULT(0);
                    default:
                        break;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // An exception must never reach Windows; the selection ends instead.
                Finish(Failed());
                return new LRESULT(0);
            }

            return PInvoke.DefWindowProc(handle, message, wParam, lParam);
        }

        private static PixelPoint Cursor() => PInvoke.GetCursorPos(out var point) ? new PixelPoint(point.X, point.Y) : default;

        private static PixelRect Between(PixelPoint a, PixelPoint b) =>
            new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    // One dimmed window over one monitor, with the pixel buffer it is drawn from.
    private sealed class OverlayWindow : IDisposable
    {
        private readonly HDC _memory;
        private readonly HBITMAP _bitmap;
        private readonly HGDIOBJ _previous;
        private readonly uint* _bits;
        private readonly int _border;
        private bool _disposed;

        private OverlayWindow(HWND handle, MonitorBounds monitor, HDC memory, HBITMAP bitmap, HGDIOBJ previous, uint* bits)
        {
            Handle = handle;
            Monitor = monitor;
            _memory = memory;
            _bitmap = bitmap;
            _previous = previous;
            _bits = bits;
            _border = RegionMath.Scaled(BorderUnits, monitor.DpiScale);
        }

        public HWND Handle { get; }

        public MonitorBounds Monitor { get; }

        public static OverlayWindow? Create(string className, HINSTANCE instance, MonitorBounds monitor)
        {
            var bounds = monitor.Bounds;
            HWND handle;
            fixed (char* name = className)
            {
                handle = PInvoke.CreateWindowEx(
                    WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
                    name,
                    null,
                    WINDOW_STYLE.WS_POPUP,
                    bounds.X,
                    bounds.Y,
                    bounds.Width,
                    bounds.Height,
                    HWND.Null,
                    HMENU.Null,
                    instance,
                    null);
            }

            if (handle.IsNull)
            {
                return null;
            }

            // Never in the pixels that are read: asked for every time, whatever the user's screen-capture setting says.
            _ = new Win32WindowEffects().ExcludeFromCapture(new WindowHandle(handle));

            var screen = PInvoke.GetDC(HWND.Null);
            var memory = PInvoke.CreateCompatibleDC(screen);
            _ = PInvoke.ReleaseDC(HWND.Null, screen);
            var header = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)sizeof(BITMAPINFOHEADER),
                    biWidth = bounds.Width,
                    biHeight = -bounds.Height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                },
            };

            void* bits = null;
            var bitmap = PInvoke.CreateDIBSection(memory, &header, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
            if (memory.IsNull || bitmap.IsNull || bits is null)
            {
                if (!bitmap.IsNull)
                {
                    _ = PInvoke.DeleteObject(bitmap);
                }

                if (!memory.IsNull)
                {
                    _ = PInvoke.DeleteDC(memory);
                }

                _ = PInvoke.DestroyWindow(handle);
                return null;
            }

            var previous = PInvoke.SelectObject(memory, bitmap);
            var window = new OverlayWindow(handle, monitor, memory, bitmap, previous, (uint*)bits);
            window.Redraw(null);
            return window;
        }

        public void Show() =>
            _ = PInvoke.SetWindowPos(Handle, HWND.HWND_TOPMOST, Monitor.Bounds.X, Monitor.Bounds.Y, Monitor.Bounds.Width, Monitor.Bounds.Height, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);

        // Draws the dim, the chosen area (in screen pixels, or null) and its border, and puts it on the screen.
        public void Redraw(PixelRect? selection)
        {
            var bounds = Monitor.Bounds;
            var local = selection is { } chosen ? RegionMath.ToLocal(chosen, bounds) : null;
            OverlayRenderer.Draw(new Span<uint>(_bits, bounds.Width * bounds.Height), bounds.Width, bounds.Height, local, _border);

            var origin = new global::System.Drawing.Point(bounds.X, bounds.Y);
            var size = new global::System.Drawing.Size(bounds.Width, bounds.Height);
            var source = new global::System.Drawing.Point(0, 0);
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            _ = PInvoke.UpdateLayeredWindow(Handle, HDC.Null, origin, size, _memory, source, default, blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _ = PInvoke.DestroyWindow(Handle);
            _ = PInvoke.SelectObject(_memory, _previous);
            _ = PInvoke.DeleteObject(_bitmap);
            _ = PInvoke.DeleteDC(_memory);
        }
    }
}
