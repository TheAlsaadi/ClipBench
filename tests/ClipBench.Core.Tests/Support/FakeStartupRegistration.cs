using ClipBench.Core.Hosting;
using ClipBench.Core.Results;

namespace ClipBench.Core.Tests.Support;

/// <summary>An in-memory stand-in for the list of programs Windows starts at sign-in.</summary>
internal sealed class FakeStartupRegistration : IStartupRegistration
{
    /// <summary>The command that is registered, or null for none.</summary>
    public string? Command { get; set; }

    /// <summary>When set, every call fails with this error.</summary>
    public ResultError? Failure { get; set; }

    public List<string> Written { get; } = [];

    public int Removals { get; private set; }

    public Result<string?> GetCommand() =>
        Failure is { } error ? Result.Failure<string?>(error) : Result.Success(Command);

    public Result SetCommand(string command)
    {
        if (Failure is { } error)
        {
            return Result.Failure(error);
        }

        Command = command;
        Written.Add(command);
        return Result.Success();
    }

    public Result Remove()
    {
        if (Failure is { } error)
        {
            return Result.Failure(error);
        }

        Command = null;
        Removals++;
        return Result.Success();
    }
}
