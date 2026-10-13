namespace ClipBench.Core.Capture;

/// <summary>Names of clipboard formats that carry a meaning for privacy or for ClipBench itself.</summary>
public static class ClipboardFormatNames
{
    /// <summary>Set by applications such as password managers to say "do not process this content".</summary>
    public const string ExcludeFromMonitorProcessing = "ExcludeClipboardContentFromMonitorProcessing";

    /// <summary>The older marker that asks clipboard viewers to ignore the content.</summary>
    public const string ViewerIgnore = "Clipboard Viewer Ignore";

    /// <summary>A flag whose value 0 means the content must not appear in clipboard history.</summary>
    public const string CanIncludeInClipboardHistory = "CanIncludeInClipboardHistory";

    /// <summary>Added by ClipBench to its own clipboard writes so it can recognise and ignore them.</summary>
    public const string OwnWrite = "ClipBench.Internal";
}
