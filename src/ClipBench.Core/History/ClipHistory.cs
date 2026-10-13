using System.Collections.Immutable;
using ClipBench.Core.Clips;
using ClipBench.Core.Results;

namespace ClipBench.Core.History;

/// <summary>
/// The ordered collection of clips, newest first, limited by count and age. Safe to use from many
/// threads: every change happens under one lock and readers get immutable snapshots.
/// </summary>
/// <remarks>
/// The count and age limits apply to unpinned clips only. They are enforced when a clip is added and
/// when <see cref="Trim"/> is called, so after <see cref="Unpin"/> the history can hold more than
/// <see cref="HistoryOptions.MaxCount"/> unpinned clips until the next add or trim. To restore a saved
/// history, add the clips oldest first.
/// </remarks>
public sealed class ClipHistory
{
    private readonly HistoryOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private ImmutableArray<Clip> _clips = [];

    /// <summary>Creates an empty history.</summary>
    /// <param name="options">The limits.</param>
    /// <param name="timeProvider">The clock used for age limits.</param>
    public ClipHistory(HistoryOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <summary>A snapshot of the clips, newest first. It does not change when the history does.</summary>
    public ImmutableArray<Clip> Clips
    {
        get
        {
            lock (_gate)
            {
                return _clips;
            }
        }
    }

    /// <summary>How many clips the history holds.</summary>
    public int Count => Clips.Length;

    /// <summary>Finds a clip.</summary>
    /// <param name="id">The clip id.</param>
    /// <returns>The clip, or null when it is not in the history.</returns>
    public Clip? Get(ClipId id)
    {
        lock (_gate)
        {
            var index = IndexOf(id);
            return index < 0 ? null : _clips[index];
        }
    }

    /// <summary>
    /// Adds a clip at the top, or moves the existing clip with the same content there. A duplicate keeps
    /// its id and pin and takes the new capture time and source. A clip that is already older than the
    /// age limit is added and evicted in the same call.
    /// </summary>
    /// <param name="clip">The clip to add.</param>
    /// <returns>What was stored and what the limits removed.</returns>
    public AddResult Add(Clip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        lock (_gate)
        {
            var clips = _clips.ToBuilder();
            var existingIndex = IndexOfContent(clips, clip.Content);
            var wasDuplicate = existingIndex >= 0;
            var stored = clip;

            if (wasDuplicate)
            {
                var existing = clips[existingIndex];
                stored = existing with
                {
                    CapturedAt = clip.CapturedAt,
                    SourceApp = clip.SourceApp,
                    IsPinned = existing.IsPinned || clip.IsPinned,
                };
                clips.RemoveAt(existingIndex);
            }

            clips.Insert(0, stored);
            _clips = clips.ToImmutable();

            return new AddResult(stored, wasDuplicate, TrimLocked());
        }
    }

    /// <summary>Protects a clip from the history limits.</summary>
    /// <param name="id">The clip id.</param>
    /// <returns>The pinned clip, or a failure when the id is unknown.</returns>
    public Result<Clip> Pin(ClipId id) => SetPinned(id, pinned: true);

    /// <summary>Makes a clip subject to the history limits again.</summary>
    /// <param name="id">The clip id.</param>
    /// <returns>The unpinned clip, or a failure when the id is unknown.</returns>
    public Result<Clip> Unpin(ClipId id) => SetPinned(id, pinned: false);

    /// <summary>Removes a clip, pinned or not.</summary>
    /// <param name="id">The clip id.</param>
    /// <returns>The removed clip, or null when it was not in the history.</returns>
    public Clip? Remove(ClipId id)
    {
        lock (_gate)
        {
            var index = IndexOf(id);
            if (index < 0)
            {
                return null;
            }

            var removed = _clips[index];
            _clips = _clips.RemoveAt(index);
            return removed;
        }
    }

    /// <summary>Removes unpinned clips, and pinned ones too when asked.</summary>
    /// <param name="includePinned">True to remove pinned clips as well.</param>
    /// <returns>The clips that were removed.</returns>
    public ImmutableArray<Clip> Clear(bool includePinned = false)
    {
        lock (_gate)
        {
            var removed = includePinned ? _clips : [.. _clips.Where(clip => !clip.IsPinned)];
            _clips = includePinned ? [] : [.. _clips.Where(clip => clip.IsPinned)];
            return removed;
        }
    }

    /// <summary>Applies the count and age limits to unpinned clips.</summary>
    /// <returns>The clips that were removed.</returns>
    public ImmutableArray<Clip> Trim()
    {
        lock (_gate)
        {
            return TrimLocked();
        }
    }

    private Result<Clip> SetPinned(ClipId id, bool pinned)
    {
        lock (_gate)
        {
            var index = IndexOf(id);
            if (index < 0)
            {
                return Result.Failure<Clip>(new ResultError(ErrorCode.ClipNotFound, "That clip is no longer in the history."));
            }

            var updated = _clips[index] with { IsPinned = pinned };
            _clips = _clips.SetItem(index, updated);
            return Result.Success(updated);
        }
    }

    private int IndexOf(ClipId id)
    {
        for (var i = 0; i < _clips.Length; i++)
        {
            if (_clips[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfContent(ImmutableArray<Clip>.Builder clips, ClipContent content)
    {
        for (var i = 0; i < clips.Count; i++)
        {
            if (clips[i].Content.Equals(content))
            {
                return i;
            }
        }

        return -1;
    }

    // The list is newest first, so walking it keeps the newest unpinned clips and evicts the rest.
    // The size budget is a prefix rule: once a clip does not fit, every older unpinned clip goes too,
    // even a small one that would fit, so the history never keeps an older clip over a newer one.
    private ImmutableArray<Clip> TrimLocked()
    {
        var now = _timeProvider.GetUtcNow();
        var kept = ImmutableArray.CreateBuilder<Clip>(_clips.Length);
        var evicted = ImmutableArray.CreateBuilder<Clip>();
        var unpinnedKept = 0;
        var unpinnedBytes = 0L;
        var budgetSpent = false;

        foreach (var clip in _clips)
        {
            if (clip.IsPinned)
            {
                kept.Add(clip);
                continue;
            }

            // A clip that is too old or over the count goes before the budget is looked at, so it never
            // counts toward the budget and cannot be the one that spends it.
            if (now - clip.CapturedAt > _options.MaxAge || unpinnedKept >= _options.MaxCount)
            {
                evicted.Add(clip);
                continue;
            }

            var size = clip.SizeInBytes;
            if (!budgetSpent && unpinnedKept > 0 && unpinnedBytes + size > _options.MaxTotalBytes)
            {
                budgetSpent = true;
            }

            if (budgetSpent)
            {
                evicted.Add(clip);
            }
            else
            {
                kept.Add(clip);
                unpinnedKept++;
                unpinnedBytes += size;
            }
        }

        if (evicted.Count > 0)
        {
            _clips = kept.ToImmutable();
        }

        return evicted.ToImmutable();
    }
}
