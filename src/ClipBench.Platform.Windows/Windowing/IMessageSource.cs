namespace ClipBench.Platform.Windows.Windowing;

/// <summary>Something that receives window messages and passes selected ones on, on its own thread.</summary>
internal interface IMessageSource
{
    /// <summary>
    /// Adds a handler for a window message. The handler runs on the window thread inside the message loop, so it
    /// must not block.
    /// </summary>
    /// <param name="message">The message number.</param>
    /// <param name="handler">Receives the message's two parameters; returns true when it dealt with the message.</param>
    void AddHandler(uint message, Func<nint, nint, bool> handler);
}
