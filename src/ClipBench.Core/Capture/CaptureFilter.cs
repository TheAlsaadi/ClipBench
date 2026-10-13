using System.Collections.Immutable;
using ClipBench.Core.Clips;

namespace ClipBench.Core.Capture;

/// <summary>
/// Decides whether a clipboard change is recorded. Immutable: turn incognito on or change the
/// exclusion list with a <c>with</c> expression and keep the new filter.
/// </summary>
public sealed record CaptureFilter
{
    private const string ExecutableSuffix = ".exe";

    /// <summary>True when nothing is captured.</summary>
    public bool Incognito { get; init; }

    /// <summary>Executable names whose clipboard changes are never captured.</summary>
    public ImmutableArray<string> ExcludedProcesses { get; init; } = [];

    /// <summary>The size limits.</summary>
    public CaptureLimits Limits { get; init; } = CaptureLimits.Default;

    /// <summary>
    /// Decides about one clipboard change. The reasons are checked in this order: incognito, ClipBench's
    /// own writes, privacy markers, excluded applications, then size.
    /// </summary>
    /// <param name="request">What is known about the change.</param>
    /// <returns>Accepted, or the first reason it is rejected.</returns>
    public CaptureDecision Evaluate(CaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Incognito)
        {
            return new CaptureDecision(CaptureRejection.Incognito);
        }

        if (request.IsOwnWrite)
        {
            return new CaptureDecision(CaptureRejection.OwnWrite);
        }

        if (HasPrivacyMarker(request))
        {
            return new CaptureDecision(CaptureRejection.PrivacyMarker);
        }

        if (IsExcluded(request.SourceProcess))
        {
            return new CaptureDecision(CaptureRejection.ExcludedProcess);
        }

        if (request.SizeInBytes > LimitFor(request.Format))
        {
            return new CaptureDecision(CaptureRejection.TooLarge);
        }

        return default;
    }

    // Fails closed: a history flag that is present but unreadable is treated as "do not record".
    private static bool HasPrivacyMarker(CaptureRequest request) =>
        request.HasFormat(ClipboardFormatNames.ExcludeFromMonitorProcessing)
        || request.HasFormat(ClipboardFormatNames.ViewerIgnore)
        || request.CanIncludeInClipboardHistory == 0
        || (request.HasFormat(ClipboardFormatNames.CanIncludeInClipboardHistory) && request.CanIncludeInClipboardHistory is null);

    private long LimitFor(ClipFormat format) => format switch
    {
        ClipFormat.Text or ClipFormat.Html or ClipFormat.Rtf or ClipFormat.Files => Limits.MaxTextBytes,
        ClipFormat.Image => Limits.MaxImageBytes,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown clip format."),
    };

    private bool IsExcluded(string? process)
    {
        var name = NameOf(process);
        if (name.Length == 0 || ExcludedProcesses.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var excluded in ExcludedProcesses)
        {
            if (string.Equals(name, NameOf(excluded), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // "C:\Program Files\KeePass\KeePass.exe", "KeePass.exe" and "keepass" all name the same program.
    private static string NameOf(string? process)
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

        return name.EndsWith(ExecutableSuffix, StringComparison.OrdinalIgnoreCase) ? name[..^ExecutableSuffix.Length] : name;
    }
}
