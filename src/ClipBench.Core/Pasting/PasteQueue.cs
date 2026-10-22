using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using ClipBench.Core.Clips;
using ClipBench.Core.Results;

namespace ClipBench.Core.Pasting;

/// <summary>
/// The state machine behind queue paste: load clips, then paste them one per Ctrl+V. It only decides
/// what happens next; the platform layer does the pasting. Safe to use from many threads: the hotkey
/// callback, the Esc handler and the tray can all call it.
/// </summary>
/// <remarks>
/// A paste is two steps so the platform can stop listening for Ctrl+V while it injects its own:
/// <see cref="Next"/> hands out a clip and moves to <see cref="QueueState.Pasting"/>, and
/// <see cref="Complete"/> moves on once the clip was pasted. <see cref="Cancel"/> works in any state.
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Queue is the domain term (see GLOSSARY.md). This type is a state machine, not a collection.")]
public sealed class PasteQueue
{
    private readonly Lock _gate = new();
    private ImmutableArray<Clip> _clips = [];
    private QueueState _state = QueueState.Idle;
    private int _pasted;
    private bool _loop;

    /// <summary>Where the queue is in its life.</summary>
    public QueueState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>How many clips are loaded.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _clips.Length;
            }
        }
    }

    /// <summary>How many clips have been pasted in the current round.</summary>
    public int PastedCount
    {
        get
        {
            lock (_gate)
            {
                return _pasted;
            }
        }
    }

    /// <summary>The state and both counts, read together so they always belong to the same moment.</summary>
    public QueueProgress Progress
    {
        get
        {
            lock (_gate)
            {
                return new QueueProgress(_state, _pasted, _clips.Length);
            }
        }
    }

    /// <summary>True when the queue starts over after the last clip.</summary>
    public bool IsLooping
    {
        get
        {
            lock (_gate)
            {
                return _loop;
            }
        }
    }

    /// <summary>Loads clips and waits for the first paste. A queue can be armed again once it ended.</summary>
    /// <param name="clips">The clips, in the order they are to be pasted.</param>
    /// <param name="loop">True to start over after the last clip.</param>
    /// <returns>A failure when nothing is selected or a paste is still running; the queue is then unchanged.</returns>
    public Result Arm(IReadOnlyList<Clip> clips, bool loop = false)
    {
        ArgumentNullException.ThrowIfNull(clips);

        lock (_gate)
        {
            if (_state == QueueState.Pasting)
            {
                return Result.Failure(new ResultError(ErrorCode.QueuePasteInProgress, "Wait for the current paste to finish."));
            }

            if (clips.Count == 0)
            {
                return Result.Failure(new ResultError(ErrorCode.NothingSelected, "Select at least one clip to queue."));
            }

            _clips = [.. clips];
            _pasted = 0;
            _loop = loop;
            _state = QueueState.Armed;
            return Result.Success();
        }
    }

    /// <summary>Hands out the next clip to paste.</summary>
    /// <returns>The clip, or a failure when the queue is not armed or a paste is still running.</returns>
    public Result<Clip> Next()
    {
        lock (_gate)
        {
            switch (_state)
            {
                case QueueState.Armed:
                    _state = QueueState.Pasting;
                    return Result.Success(_clips[_pasted]);
                case QueueState.Pasting:
                    return Result.Failure<Clip>(new ResultError(ErrorCode.QueuePasteInProgress, "Wait for the current paste to finish."));
                default:
                    return Result.Failure<Clip>(new ResultError(ErrorCode.QueueNotArmed, "The queue is not armed."));
            }
        }
    }

    /// <summary>Reports that the clip handed out by <see cref="Next"/> was pasted.</summary>
    /// <returns>A failure when no paste is running, for example because the queue was cancelled meanwhile.</returns>
    public Result Complete()
    {
        lock (_gate)
        {
            if (_state != QueueState.Pasting)
            {
                return Result.Failure(new ResultError(ErrorCode.QueueNotPasting, "No paste is running."));
            }

            _pasted++;
            if (_pasted < _clips.Length)
            {
                _state = QueueState.Armed;
            }
            else if (_loop)
            {
                _pasted = 0;
                _state = QueueState.Armed;
            }
            else
            {
                _state = QueueState.Exhausted;
            }

            return Result.Success();
        }
    }

    /// <summary>Stops a queue that is armed or pasting. In any other state it does nothing.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            if (_state is QueueState.Armed or QueueState.Pasting)
            {
                _state = QueueState.Cancelled;
            }
        }
    }
}
