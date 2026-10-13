namespace ClipBench.Platform.Windows.Clipboard;

/// <summary>Finds which program put the current content on the clipboard.</summary>
internal static class OwnerProcess
{
    /// <summary>
    /// Follows the owner window of the clipboard to its process and that process's program. Each step can
    /// fail without harm: a clipboard can have no owner window, a window can vanish, and Windows refuses
    /// to describe a protected or higher-privileged process. ClipBench asks for no more rights than the
    /// query needs and never tries again with more.
    /// </summary>
    /// <param name="lookup">The Windows calls.</param>
    /// <returns>The executable's file name, for example <c>KeePass.exe</c>; never the path, and never a window title. Null when it cannot be found.</returns>
    public static string? Resolve(IProcessLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        var window = lookup.GetClipboardOwnerWindow();
        if (window == 0)
        {
            return null;
        }

        var process = lookup.GetWindowProcessId(window);
        if (process == 0)
        {
            return null;
        }

        return FileNameOf(lookup.GetProcessImagePath(process));
    }

    /// <summary>The file name of a program's path, which is all ClipBench ever keeps of it.</summary>
    /// <param name="path">The path, or null.</param>
    /// <returns>For example <c>KeePass.exe</c>; null when there is no usable name.</returns>
    public static string? FileNameOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var name = Path.GetFileName(path.Trim());
        return name.Length == 0 ? null : name;
    }
}
