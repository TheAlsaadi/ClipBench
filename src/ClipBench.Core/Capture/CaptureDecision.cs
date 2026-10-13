namespace ClipBench.Core.Capture;

/// <summary>Why a clipboard change is not recorded.</summary>
public enum CaptureRejection
{
    /// <summary>Not rejected: the change is recorded.</summary>
    None = 0,

    /// <summary>Incognito is on, so nothing is recorded.</summary>
    Incognito,

    /// <summary>ClipBench put this content on the clipboard itself.</summary>
    OwnWrite,

    /// <summary>The source application asked for the content not to be recorded.</summary>
    PrivacyMarker,

    /// <summary>The source application is on the exclusion list.</summary>
    ExcludedProcess,

    /// <summary>The content is larger than the capture limit.</summary>
    TooLarge,
}

/// <summary>Whether a clipboard change is recorded and, if not, why.</summary>
/// <param name="Rejection">The reason it is rejected, or <see cref="CaptureRejection.None"/> when it is accepted.</param>
public readonly record struct CaptureDecision(CaptureRejection Rejection)
{
    /// <summary>True when the change is recorded.</summary>
    public bool IsAccepted => Rejection == CaptureRejection.None;
}
