namespace ClipBench.Platform.Windows.Hotkeys;

/// <summary>Claiming and releasing global shortcuts with Windows. Both calls must run on the thread of the window that receives the presses.</summary>
internal interface IHotkeyApi
{
    /// <summary>Claims a shortcut.</summary>
    /// <param name="id">The number the shortcut will be known by in the message Windows sends.</param>
    /// <param name="modifiers">Windows' modifier flags.</param>
    /// <param name="virtualKey">The key.</param>
    /// <param name="error">The Windows error number when this returns false.</param>
    /// <returns>True when the shortcut is now ClipBench's.</returns>
    bool Register(int id, uint modifiers, uint virtualKey, out int error);

    /// <summary>Releases a shortcut.</summary>
    /// <param name="id">The number it was claimed under.</param>
    /// <returns>True when it was released.</returns>
    bool Unregister(int id);
}
