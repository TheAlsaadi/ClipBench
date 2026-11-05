namespace ClipBench.Core.Privacy;

/// <summary>Reduces the many ways to write the name of a program to one, so that they can be compared.</summary>
internal static class ProcessName
{
    private const string ExecutableSuffix = ".exe";

    /// <summary>
    /// "C:\Program Files\KeePass\KeePass.exe", "KeePass.exe" and "keepass" all name the same program: this drops the
    /// folder and the <c>.exe</c> ending and trims the white space. The case is kept.
    /// </summary>
    /// <param name="process">A program name or path as written by the user or reported by Windows.</param>
    /// <returns>The bare name, or an empty string when there is none.</returns>
    public static string Normalize(string? process)
    {
        if (string.IsNullOrWhiteSpace(process))
        {
            return string.Empty;
        }

        var name = process.Trim();
        var lastSeparator = name.LastIndexOfAny(['\\', '/']);
        if (lastSeparator >= 0)
        {
            name = name[(lastSeparator + 1)..].Trim();
        }

        return name.EndsWith(ExecutableSuffix, StringComparison.OrdinalIgnoreCase) ? name[..^ExecutableSuffix.Length].TrimEnd() : name;
    }
}
