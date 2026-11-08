using ClipBench.Presentation.Hosting;
using CommunityToolkit.Mvvm.Input;

namespace ClipBench.Presentation.Tests.Hosting;

public class CommandRunnerTests
{
    [Fact]
    public async Task A_command_that_is_still_running_is_not_started_again_and_its_token_is_not_cancelled()
    {
        // Key repeat sends the same key many times a second. The toolkit's Execute does not ask CanExecute, and starting an
        // async command again cancels the run before it, which crashed the app or left a deleted clip on disk.
        var release = new TaskCompletionSource();
        var runs = 0;
        CancellationToken first = default;
        var command = new AsyncRelayCommand(async token =>
        {
            if (Interlocked.Increment(ref runs) == 1)
            {
                first = token;
            }

            await release.Task;
        });

        command.TryExecute().ShouldBeTrue();
        command.TryExecute().ShouldBeFalse();
        command.TryExecute().ShouldBeFalse();

        runs.ShouldBe(1);
        first.IsCancellationRequested.ShouldBeFalse();
        release.SetResult();
        await command.ExecutionTask!;
        command.TryExecute().ShouldBeTrue();
        runs.ShouldBe(2);
    }

    [Fact]
    public void A_command_with_a_parameter_receives_it()
    {
        int? seen = null;
        var command = new RelayCommand<int>(value => seen = value);

        command.TryExecute(7).ShouldBeTrue();

        seen.ShouldBe(7);
    }

    [Fact]
    public void A_missing_command_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => CommandRunner.TryExecute(null!));
    }
}
