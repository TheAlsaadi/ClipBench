namespace ClipBench.Core.Pasting;

/// <summary>Identifies a window on screen. The value is the operating system's window handle, held as a number so Core stays free of Windows types.</summary>
/// <param name="Value">The handle. Zero means no window.</param>
public readonly record struct WindowHandle(long Value)
{
    /// <summary>No window.</summary>
    public static WindowHandle None => default;

    /// <summary>True when this is no window.</summary>
    public bool IsNone => Value == 0;

    /// <summary>Printing shows the handle in hex, which is how debugging tools show it.</summary>
    /// <returns>For example <c>Window 0x1A2B</c>.</returns>
    public override string ToString() => IsNone ? "No window" : $"Window 0x{Value:X}";
}
