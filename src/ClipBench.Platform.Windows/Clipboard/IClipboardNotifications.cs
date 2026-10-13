namespace ClipBench.Platform.Windows.Clipboard;

/// <summary>Asking Windows to send the window a message whenever the clipboard changes. Both calls must run on the window's thread.</summary>
internal interface IClipboardNotifications
{
    /// <summary>Starts the messages.</summary>
    /// <returns>True when Windows accepted.</returns>
    bool Subscribe();

    /// <summary>Stops the messages.</summary>
    void Unsubscribe();
}
