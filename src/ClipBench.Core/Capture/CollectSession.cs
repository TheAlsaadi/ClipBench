using System.Text;
using ClipBench.Core.Clips;
using ClipBench.Core.Pasting;

namespace ClipBench.Core.Capture;

/// <summary>
/// A period in which every copy is appended to one growing clip until the user finishes it.
/// Safe to use from many threads. The collected text is capped, so a long session cannot grow without bound.
/// </summary>
public sealed class CollectSession
{
    /// <summary>The default limit on the collected text, in UTF-8 bytes.</summary>
    public const long DefaultMaxBytes = 2L * 1024 * 1024;

    private readonly Separator _separator;
    private readonly int _separatorBytes;
    private readonly TimeProvider _timeProvider;
    private readonly Func<ClipId> _newId;
    private readonly long _maxBytes;
    private readonly Lock _gate = new();
    private readonly List<string> _parts = [];
    private long _bytes;
    private bool _isActive;

    /// <summary>Creates a session that is not collecting yet.</summary>
    /// <param name="separator">The text placed between the collected captures.</param>
    /// <param name="timeProvider">The clock that stamps the finished clip.</param>
    /// <param name="newId">Makes the id of the finished clip.</param>
    /// <param name="maxBytes">The most UTF-8 bytes the collected text may have.</param>
    public CollectSession(Separator separator, TimeProvider timeProvider, Func<ClipId> newId, long maxBytes = DefaultMaxBytes)
    {
        ArgumentNullException.ThrowIfNull(separator);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(newId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);

        _separator = separator;
        _separatorBytes = Encoding.UTF8.GetByteCount(separator.Text);
        _timeProvider = timeProvider;
        _newId = newId;
        _maxBytes = maxBytes;
    }

    /// <summary>True while the session is collecting.</summary>
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _isActive;
            }
        }
    }

    /// <summary>How many captures have been collected.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _parts.Count;
            }
        }
    }

    /// <summary>The size in UTF-8 bytes of the text collected so far, separators included.</summary>
    public long SizeInBytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    /// <summary>Starts collecting. If a session is already running it keeps what it has.</summary>
    public void Start()
    {
        lock (_gate)
        {
            _isActive = true;
        }
    }

    /// <summary>Offers a capture to the session.</summary>
    /// <param name="request">What is known about the clipboard change; used to recognise ClipBench's own writes.</param>
    /// <param name="clip">The captured clip.</param>
    /// <returns>Whether the capture was collected and, if not, why.</returns>
    public AppendOutcome Append(CaptureRequest request, Clip clip)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(clip);

        lock (_gate)
        {
            if (!_isActive)
            {
                return AppendOutcome.NotCollecting;
            }

            if (request.IsOwnWrite)
            {
                return AppendOutcome.OwnWrite;
            }

            if (clip.Content.PlainText is not { } text)
            {
                return AppendOutcome.NotText;
            }

            var added = (long)Encoding.UTF8.GetByteCount(text) + (_parts.Count > 0 ? _separatorBytes : 0);
            if (_bytes + added > _maxBytes)
            {
                return AppendOutcome.LimitReached;
            }

            _parts.Add(text);
            _bytes += added;
            return AppendOutcome.Appended;
        }
    }

    /// <summary>Ends the session and returns everything collected as one text clip.</summary>
    /// <returns>The clip, or null when nothing was collected or no session was running.</returns>
    public Clip? Finish()
    {
        string? text;
        lock (_gate)
        {
            text = _parts.Count == 0 ? null : string.Join(_separator.Text, _parts);
            EndLocked();
        }

        // The caller's id factory runs outside the lock so it can never block or re-enter the session.
        return text is null ? null : new Clip(_newId(), new TextContent(text), _timeProvider.GetUtcNow());
    }

    /// <summary>Ends the session and throws away what was collected.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            EndLocked();
        }
    }

    private void EndLocked()
    {
        _isActive = false;
        _parts.Clear();
        _bytes = 0;
    }
}
