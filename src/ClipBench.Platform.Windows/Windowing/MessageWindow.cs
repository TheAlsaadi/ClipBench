using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using ClipBench.Core.Results;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ClipBench.Platform.Windows.Windowing;

/// <summary>Runs work on a given thread.</summary>
internal interface IThreadInvoker
{
    /// <summary>Runs the work on the thread and returns its result.</summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="work">The work. It runs on the invoker's thread.</param>
    /// <param name="cancellationToken">Cancels the work if it has not started.</param>
    /// <returns>The result of the work, or the exception it threw.</returns>
    Task<T> InvokeAsync<T>(Func<T> work, CancellationToken cancellationToken);
}

/// <summary>
/// An invisible message-only window on a thread of its own. Windows ties some calls to the thread that owns
/// a window (registering a hotkey, receiving a clipboard-change notice, owning the clipboard), so
/// everything of that kind runs here: one thread, one queue, one place to look when something goes wrong.
/// </summary>
internal sealed unsafe class MessageWindow : IThreadInvoker, IDisposable
{
    private const uint RunMessage = PInvoke.WM_APP + 1;
    private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(5);

    private readonly string _className = "ClipBench.MessageWindow." + Guid.NewGuid().ToString("N");
    private readonly ConcurrentQueue<IWorkItem> _queue = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly WNDPROC _windowProcedure;
    private readonly Thread _thread;
    private ImmutableDictionary<uint, ImmutableArray<Func<nint, nint, bool>>> _handlers = ImmutableDictionary<uint, ImmutableArray<Func<nint, nint, bool>>>.Empty;
    private HWND _handle;
    private ResultError? _startFailure;
    private int _disposed;

    private MessageWindow()
    {
        _windowProcedure = WindowProcedure;
        _thread = new Thread(Run) { IsBackground = true, Name = "ClipBench message window" };
    }

    /// <summary>The window handle, as a number.</summary>
    public nint Handle => _handle;

    /// <summary>True once disposal has begun. Work that has not started by then fails.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Starts the thread and creates the window.</summary>
    /// <returns>The window, or <see cref="ErrorCode.PlatformFailure"/> when Windows would not create it.</returns>
    public static Result<MessageWindow> Create()
    {
        var window = new MessageWindow();
        window._thread.Start();
        if (!window._ready.Wait(_startTimeout))
        {
            return Result.Failure<MessageWindow>(new ResultError(ErrorCode.PlatformFailure, "Windows did not set up ClipBench's background window in time."));
        }

        if (window._startFailure is { } failure)
        {
            window._thread.Join();
            return Result.Failure<MessageWindow>(failure);
        }

        return Result.Success(window);
    }

    /// <summary>
    /// Adds a handler for a window message. Handlers run on the window thread inside the message loop,
    /// so they must not block. A handler that throws is ignored: an exception must never reach Windows.
    /// </summary>
    /// <param name="message">The message number, for example WM_HOTKEY.</param>
    /// <param name="handler">Receives the message's two parameters; returns true when it dealt with the message.</param>
    public void AddHandler(uint message, Func<nint, nint, bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ImmutableInterlocked.AddOrUpdate(
            ref _handlers,
            message,
            _ => [handler],
            (_, existing) => existing.Add(handler));
    }

    /// <inheritdoc/>
    public Task<T> InvokeAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (IsDisposed)
        {
            return Task.FromException<T>(new ObjectDisposedException(nameof(MessageWindow)));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var item = new WorkItem<T>(work, cancellationToken);
        _queue.Enqueue(item);
        if (!PInvoke.PostMessage(_handle, RunMessage, default, default) || IsDisposed)
        {
            // Disposal may have drained the queue just before this item was added. Draining again settles it.
            FailPending();
        }

        return item.Task;
    }

    /// <summary>Destroys the window and stops the thread. Work that has not started fails with <see cref="ObjectDisposedException"/>.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        PInvoke.PostMessage(_handle, PInvoke.WM_CLOSE, default, default);
        if (Environment.CurrentManagedThreadId != _thread.ManagedThreadId)
        {
            _thread.Join(_stopTimeout);
        }

        FailPending();
        _ready.Dispose();
    }

    private void Run()
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
                    lpfnWndProc = _windowProcedure,
                    hInstance = instance,
                    lpszClassName = className,
                };

                registered = PInvoke.RegisterClassEx(in windowClass) != 0;
                if (registered)
                {
                    _handle = PInvoke.CreateWindowEx(0, className, null, 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, HMENU.Null, instance, null);
                }
            }

            if (_handle.IsNull)
            {
                _startFailure = new ResultError(ErrorCode.PlatformFailure, "Windows would not create ClipBench's background window.");
                _ready.Set();
                return;
            }

            _ready.Set();

            // GetMessage returns -1 on error, which would read as "true" if treated as a flag.
            while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0).Value > 0)
            {
                PInvoke.DispatchMessage(in message);
            }
        }
        finally
        {
            if (registered)
            {
                fixed (char* className = _className)
                {
                    PInvoke.UnregisterClass(className, instance);
                }
            }
        }
    }

    private LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (message == RunMessage)
            {
                RunQueued();
                return default;
            }

            if (message == PInvoke.WM_DESTROY)
            {
                PInvoke.PostQuitMessage(0);
                return default;
            }

            if (_handlers.TryGetValue(message, out var handlers))
            {
                var handled = false;
                foreach (var handler in handlers)
                {
                    handled |= RunHandler(handler, (nint)wParam.Value, lParam.Value);
                }

                if (handled)
                {
                    return default;
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Nothing may escape into Windows. The work items report their own failures to their callers.
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private static bool RunHandler(Func<nint, nint, bool> handler, nint wParam, nint lParam)
    {
        try
        {
            return handler(wParam, lParam);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    private void RunQueued()
    {
        while (_queue.TryDequeue(out var item))
        {
            if (IsDisposed)
            {
                item.Fail(new ObjectDisposedException(nameof(MessageWindow)));
            }
            else
            {
                item.Run();
            }
        }
    }

    private void FailPending()
    {
        while (_queue.TryDequeue(out var item))
        {
            item.Fail(new ObjectDisposedException(nameof(MessageWindow)));
        }
    }

    private interface IWorkItem
    {
        void Run();

        void Fail(Exception exception);
    }

    private sealed class WorkItem<T> : IWorkItem
    {
        private readonly Func<T> _work;
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;

        public WorkItem(Func<T> work, CancellationToken cancellationToken)
        {
            _work = work;
            if (cancellationToken.CanBeCanceled)
            {
                _registration = cancellationToken.Register(() => _completion.TrySetCanceled(cancellationToken));
            }
        }

        public Task<T> Task => _completion.Task;

        public void Run()
        {
            _registration.Dispose();
            if (_completion.Task.IsCompleted)
            {
                return;   // cancelled while it waited
            }

            try
            {
                _completion.TrySetResult(_work());
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }

        public void Fail(Exception exception)
        {
            _registration.Dispose();
            _completion.TrySetException(exception);
        }
    }
}
