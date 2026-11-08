using System.Windows.Input;

namespace ClipBench.Presentation.Hosting;

/// <summary>
/// The way views run a command from a key or a menu. <see cref="ICommand.Execute"/> on the toolkit's async commands does not
/// ask <see cref="ICommand.CanExecute"/>: a second run starts while the first is still going and cancels its token, so a held
/// key could stop a delete half way or cut a paste short. A command that is still running is skipped instead.
/// </summary>
public static class CommandRunner
{
    /// <summary>Runs the command when it can run now.</summary>
    /// <param name="command">The command.</param>
    /// <param name="parameter">The command's parameter.</param>
    /// <returns>True when the command was started.</returns>
    public static bool TryExecute(this ICommand command, object? parameter = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!command.CanExecute(parameter))
        {
            return false;
        }

        command.Execute(parameter);
        return true;
    }
}
