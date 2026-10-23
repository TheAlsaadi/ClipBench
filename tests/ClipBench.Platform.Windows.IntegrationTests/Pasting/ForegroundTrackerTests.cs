using ClipBench.Core.Pasting;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.IntegrationTests.Support;
using ClipBench.Platform.Windows.Pasting;

namespace ClipBench.Platform.Windows.IntegrationTests.Pasting;

public class ForegroundTrackerTests
{
    private readonly FakeInput _input = new() { Foreground = 0x100 };
    private readonly FakeIntegrity _integrity = new();
    private readonly FakeProcessLookup _processes = new() { ProcessId = 4321, ImagePath = @"C:\Windows\System32\notepad.exe" };

    private ForegroundTracker Tracker => new(_input, _processes, _integrity);

    [Fact]
    public void The_window_that_has_focus_is_recorded_with_its_process_and_program()
    {
        var result = Tracker.Capture();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Window.ShouldBe(new WindowHandle(0x100));
        result.Value.ProcessId.ShouldBe(4321);
        result.Value.ProcessName.ShouldBe("notepad.exe", "the file name, never the path");
        result.Value.BlocksInputFromUs.ShouldBeFalse();
    }

    [Fact]
    public void No_window_having_focus_is_reported()
    {
        _input.Foreground = 0;

        var result = Tracker.Capture();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.TargetNotFound);
    }

    [Fact]
    public void A_window_can_be_looked_at_again_later_by_its_handle()
    {
        var result = Tracker.Describe(new WindowHandle(0x100));

        result.Value.ProcessName.ShouldBe("notepad.exe");
    }

    [Fact]
    public void A_window_that_is_gone_is_not_found()
    {
        _input.Windows.Remove(0x100);

        var result = Tracker.Describe(new WindowHandle(0x100));

        result.Error.Code.ShouldBe(ErrorCode.TargetNotFound);
    }

    [Fact]
    public void No_window_at_all_is_not_found()
    {
        Tracker.Describe(WindowHandle.None).Error.Code.ShouldBe(ErrorCode.TargetNotFound);
    }

    [Fact]
    public void A_window_whose_process_is_unknown_is_not_found_even_if_the_process_number_zero_would_otherwise_look_fine()
    {
        _input.ProcessOf.Remove(0x100);
        _integrity.Levels[0] = new IntegrityResult(IntegrityOutcome.Known, 0x2000);   // so only the explicit check can refuse

        Tracker.Describe(new WindowHandle(0x100)).Error.Code.ShouldBe(ErrorCode.TargetNotFound);
    }

    [Fact]
    public void A_process_that_ended_in_between_is_not_found()
    {
        _integrity.Levels.Remove(4321);

        Tracker.Describe(new WindowHandle(0x100)).Error.Code.ShouldBe(ErrorCode.TargetNotFound);
    }

    [Theory]
    [InlineData(0x1000, 0x2000, false)]   // a low-integrity program: ClipBench may send to it
    [InlineData(0x2000, 0x2000, false)]   // the same level
    [InlineData(0x3000, 0x2000, true)]    // an administrator's program
    [InlineData(0x4000, 0x2000, true)]    // a system program
    [InlineData(0x3000, 0x3000, false)]   // ClipBench itself runs elevated and so may send to anything up to its own level
    public void Windows_blocks_keystrokes_only_to_a_program_with_a_higher_level_than_ours(int target, int own, bool blocks)
    {
        _integrity.OwnLevel = own;
        _integrity.Levels[4321] = new IntegrityResult(IntegrityOutcome.Known, target);

        Tracker.Capture().Value.BlocksInputFromUs.ShouldBe(blocks);
    }

    [Fact]
    public void A_process_that_refuses_to_say_what_level_it_has_is_assumed_to_block_input()
    {
        // Programs with more rights than ClipBench are exactly the ones that refuse to be queried.
        _integrity.Levels[4321] = new IntegrityResult(IntegrityOutcome.AccessDenied);

        Tracker.Capture().Value.BlocksInputFromUs.ShouldBeTrue();
    }

    [Fact]
    public void Only_the_window_in_front_is_in_front()
    {
        Tracker.IsForeground(new WindowHandle(0x100)).ShouldBeTrue();
        Tracker.IsForeground(new WindowHandle(0x900)).ShouldBeFalse();
        Tracker.IsForeground(WindowHandle.None).ShouldBeFalse();
    }

    [Fact]
    public void A_window_can_be_brought_back_to_the_front()
    {
        _input.Foreground = 0x900;

        Tracker.Restore(new WindowHandle(0x100)).ShouldBeTrue();

        _input.FocusRequests.ShouldBe([(nint)0x100]);
    }

    [Fact]
    public void A_window_that_is_already_in_front_is_not_asked_again()
    {
        Tracker.Restore(new WindowHandle(0x100)).ShouldBeTrue();

        _input.FocusRequests.ShouldBeEmpty();
    }

    [Fact]
    public void A_window_that_is_gone_cannot_be_brought_back()
    {
        _input.Foreground = 0x900;
        _input.Windows.Remove(0x100);

        Tracker.Restore(new WindowHandle(0x100)).ShouldBeFalse();
        Tracker.Restore(WindowHandle.None).ShouldBeFalse();

        _input.FocusRequests.ShouldBeEmpty();
    }

    [Fact]
    public void A_refusal_from_Windows_is_reported_because_only_the_window_in_front_is_believed()
    {
        _input.Foreground = 0x900;
        _input.FocusMovesAfterPolls = -1;

        Tracker.Restore(new WindowHandle(0x100)).ShouldBeFalse();
    }

    [Fact]
    public void A_program_Windows_will_not_name_is_still_a_target_just_without_a_name()
    {
        _processes.ImagePath = null;

        var result = Tracker.Capture();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ProcessName.ShouldBeNull();
    }
}
