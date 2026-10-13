using ClipBench.Core.Clips;
using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Pasting;

public class PasteQueueTests
{
    private static Clip[] Clips(params string[] texts) => [.. texts.Select(text => TestClips.Text(text))];

    private static PasteQueue Armed(Clip[] clips, bool loop = false)
    {
        var queue = new PasteQueue();
        queue.Arm(clips, loop).IsSuccess.ShouldBeTrue();
        return queue;
    }

    private static PasteQueue InState(QueueState state)
    {
        var clips = Clips("a", "b");
        var queue = new PasteQueue();
        switch (state)
        {
            case QueueState.Idle:
                break;
            case QueueState.Armed:
                queue.Arm(clips);
                break;
            case QueueState.Pasting:
                queue.Arm(clips);
                queue.Next();
                break;
            case QueueState.Exhausted:
                queue.Arm(clips);
                queue.Next();
                queue.Complete();
                queue.Next();
                queue.Complete();
                break;
            case QueueState.Cancelled:
                queue.Arm(clips);
                queue.Cancel();
                break;
        }

        queue.State.ShouldBe(state);
        return queue;
    }

    // --- the happy path ---

    [Fact]
    public void A_new_queue_is_idle_and_empty()
    {
        var queue = new PasteQueue();

        queue.State.ShouldBe(QueueState.Idle);
        queue.Count.ShouldBe(0);
        queue.PastedCount.ShouldBe(0);
        queue.IsLooping.ShouldBeFalse();
    }

    [Fact]
    public void Arming_loads_the_clips_and_waits_for_the_first_paste()
    {
        var queue = new PasteQueue();

        var result = queue.Arm(Clips("a", "b", "c"));

        result.IsSuccess.ShouldBeTrue();
        queue.State.ShouldBe(QueueState.Armed);
        queue.Count.ShouldBe(3);
        queue.PastedCount.ShouldBe(0);
    }

    [Fact]
    public void Arm_then_next_then_next_pastes_the_clips_in_order_and_ends_exhausted()
    {
        var clips = Clips("first", "second");
        var queue = Armed(clips);

        var first = queue.Next();
        first.Value.ShouldBe(clips[0]);
        queue.State.ShouldBe(QueueState.Pasting);

        queue.Complete().IsSuccess.ShouldBeTrue();
        queue.State.ShouldBe(QueueState.Armed);
        queue.PastedCount.ShouldBe(1);

        var second = queue.Next();
        second.Value.ShouldBe(clips[1]);
        queue.State.ShouldBe(QueueState.Pasting);

        queue.Complete().IsSuccess.ShouldBeTrue();
        queue.State.ShouldBe(QueueState.Exhausted);
        queue.PastedCount.ShouldBe(2);
    }

    [Fact]
    public void A_queue_made_from_a_selection_keeps_the_selection_order()
    {
        var clips = Clips("third", "first", "second");
        var queue = Armed(clips);
        var seen = new List<Clip>();

        // Bounded, so a faulty queue fails this test instead of looping forever.
        for (var step = 0; step <= clips.Length && queue.State != QueueState.Exhausted; step++)
        {
            seen.Add(queue.Next().Value);
            queue.Complete();
        }

        seen.ShouldBe(clips);
        queue.State.ShouldBe(QueueState.Exhausted);
    }

    [Fact]
    public void A_queue_of_one_clip_is_exhausted_after_one_paste()
    {
        var queue = Armed(Clips("only"));

        queue.Next();
        queue.Complete();

        queue.State.ShouldBe(QueueState.Exhausted);
    }

    [Fact]
    public void Images_can_be_queued()
    {
        var queue = Armed([TestClips.Image(1), TestClips.Text("text")]);

        queue.Next().Value.Content.Format.ShouldBe(ClipFormat.Image);
    }

    [Fact]
    public void The_queue_keeps_its_own_copy_of_the_selection()
    {
        var clips = Clips("a", "b");
        var list = clips.ToList();
        var queue = new PasteQueue();
        queue.Arm(list);

        list.Clear();

        queue.Count.ShouldBe(2);
        queue.Next().Value.ShouldBe(clips[0]);
    }

    // --- loop ---

    [Fact]
    public void A_looping_queue_starts_over_after_the_last_clip()
    {
        var clips = Clips("a", "b");
        var queue = Armed(clips, loop: true);

        queue.IsLooping.ShouldBeTrue();
        queue.Next();
        queue.Complete();
        queue.Next();
        queue.Complete();

        queue.State.ShouldBe(QueueState.Armed);
        queue.PastedCount.ShouldBe(0);
        queue.Next().Value.ShouldBe(clips[0]);
    }

    [Fact]
    public void A_looping_queue_stops_when_cancelled()
    {
        var queue = Armed(Clips("a", "b"), loop: true);
        queue.Next();
        queue.Complete();

        queue.Cancel();

        queue.State.ShouldBe(QueueState.Cancelled);
        queue.Next().Error.Code.ShouldBe(ErrorCode.QueueNotArmed);
    }

    // --- cancel ---

    [Theory]
    [InlineData(QueueState.Armed, QueueState.Cancelled)]
    [InlineData(QueueState.Pasting, QueueState.Cancelled)]
    [InlineData(QueueState.Idle, QueueState.Idle)]
    [InlineData(QueueState.Exhausted, QueueState.Exhausted)]
    [InlineData(QueueState.Cancelled, QueueState.Cancelled)]
    public void Cancelling_is_allowed_in_every_state_and_only_stops_a_queue_that_is_running(QueueState before, QueueState after)
    {
        var queue = InState(before);

        queue.Cancel();

        queue.State.ShouldBe(after);
    }

    [Fact]
    public void A_paste_that_finishes_after_a_cancel_changes_nothing()
    {
        var queue = Armed(Clips("a", "b"));
        queue.Next();
        queue.Cancel();

        var result = queue.Complete();

        result.IsFailure.ShouldBeTrue();
        queue.State.ShouldBe(QueueState.Cancelled);
        queue.PastedCount.ShouldBe(0);
    }

    // --- errors ---

    [Fact]
    public void Next_while_idle_is_a_no_op_with_an_error_result()
    {
        var queue = new PasteQueue();

        var result = queue.Next();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.QueueNotArmed);
        queue.State.ShouldBe(QueueState.Idle);
    }

    [Theory]
    [InlineData(QueueState.Exhausted)]
    [InlineData(QueueState.Cancelled)]
    public void Next_after_the_queue_has_ended_is_an_error_and_changes_nothing(QueueState state)
    {
        var queue = InState(state);

        queue.Next().Error.Code.ShouldBe(ErrorCode.QueueNotArmed);

        queue.State.ShouldBe(state);
    }

    [Fact]
    public void Next_while_a_paste_is_still_running_is_an_error_and_does_not_skip_a_clip()
    {
        var clips = Clips("a", "b");
        var queue = Armed(clips);
        queue.Next();

        var result = queue.Next();

        result.Error.Code.ShouldBe(ErrorCode.QueuePasteInProgress);
        queue.State.ShouldBe(QueueState.Pasting);
        queue.Complete();
        queue.Next().Value.ShouldBe(clips[1]);
    }

    [Theory]
    [InlineData(QueueState.Idle)]
    [InlineData(QueueState.Armed)]
    [InlineData(QueueState.Exhausted)]
    [InlineData(QueueState.Cancelled)]
    public void Completing_when_no_paste_is_running_is_an_error_and_changes_nothing(QueueState state)
    {
        var queue = InState(state);
        var pasted = queue.PastedCount;

        queue.Complete().Error.Code.ShouldBe(ErrorCode.QueueNotPasting);

        queue.State.ShouldBe(state);
        queue.PastedCount.ShouldBe(pasted);
    }

    // --- arming ---

    [Fact]
    public void Arming_with_nothing_selected_is_an_error_and_keeps_the_current_queue()
    {
        var queue = Armed(Clips("a"));

        var result = queue.Arm([]);

        result.Error.Code.ShouldBe(ErrorCode.NothingSelected);
        queue.State.ShouldBe(QueueState.Armed);
        queue.Count.ShouldBe(1);
    }

    [Fact]
    public void Arming_while_a_paste_is_running_is_an_error()
    {
        var queue = Armed(Clips("a", "b"));
        queue.Next();

        queue.Arm(Clips("x")).Error.Code.ShouldBe(ErrorCode.QueuePasteInProgress);

        queue.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(QueueState.Armed)]
    [InlineData(QueueState.Exhausted)]
    [InlineData(QueueState.Cancelled)]
    public void A_queue_can_be_armed_again_with_a_new_selection(QueueState state)
    {
        var queue = InState(state);
        var clips = Clips("new one", "new two", "new three");

        queue.Arm(clips, loop: true).IsSuccess.ShouldBeTrue();

        queue.State.ShouldBe(QueueState.Armed);
        queue.Count.ShouldBe(3);
        queue.PastedCount.ShouldBe(0);
        queue.IsLooping.ShouldBeTrue();
        queue.Next().Value.ShouldBe(clips[0]);
    }

    [Fact]
    public void Arming_with_no_list_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new PasteQueue().Arm(null!));
    }

    // --- threads ---

    [Fact]
    public async Task Many_threads_pasting_at_once_hand_out_every_clip_exactly_once_in_order()
    {
        var clips = Enumerable.Range(0, 200).Select(i => TestClips.Text($"clip {i}")).ToArray();
        var queue = Armed(clips);
        var handedOut = new List<Clip>();

        // A deadline, so a faulty queue fails this test instead of keeping eight threads spinning forever.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            while (!deadline.IsCancellationRequested && queue.State is not (QueueState.Exhausted or QueueState.Cancelled))
            {
                var next = queue.Next();
                if (next.IsSuccess)
                {
                    // Only the thread that holds the Pasting state gets here, so this list needs no lock.
                    handedOut.Add(next.Value);
                    queue.Complete();
                }
            }
        })));

        handedOut.ShouldBe(clips);
    }
}
