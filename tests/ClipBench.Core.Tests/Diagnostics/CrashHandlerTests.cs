using ClipBench.Core.Diagnostics;
using ClipBench.Core.Notifications;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Diagnostics;

public sealed class CrashHandlerTests
{
    private const string Notice =
        "ClipBench ran into a problem and has to close. Start it again from the Start menu. The log records only the kind of problem, never what you copied.";

    private readonly FakeActivityLog _log = new();
    private readonly FakeNotifier _notifier = new();

    private CrashHandler CreateHandler() => new(_log, _notifier);

    [Fact]
    public void A_UI_thread_crash_is_logged_with_its_type_and_closes_the_app()
    {
        var response = CreateHandler().Handle(new InvalidOperationException("boom"), CrashSource.UiThread);

        response.ShouldBe(CrashResponse.CloseApp);
        var entry = _log.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(ActivityLevel.Error);
        entry.Activity.ShouldBe(ActivityEvent.AppCrashed);
        entry.Details.ExceptionType.ShouldBe(typeof(InvalidOperationException));
        entry.Details.Source.ShouldBe(CrashSource.UiThread);
        _notifier.Shown.ShouldBe([("ClipBench", Notice)]);
    }

    [Fact]
    public void A_background_task_fault_is_logged_and_the_app_keeps_running()
    {
        var response = CreateHandler().Handle(new TimeoutException(), CrashSource.BackgroundTask);

        response.ShouldBe(CrashResponse.KeepRunning);
        var entry = _log.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(ActivityLevel.Warning);
        entry.Activity.ShouldBe(ActivityEvent.BackgroundFaulted);
        entry.Details.ExceptionType.ShouldBe(typeof(TimeoutException));
        entry.Details.Source.ShouldBe(CrashSource.BackgroundTask);
        _notifier.Shown.ShouldBeEmpty();
    }

    [Fact]
    public void A_process_crash_is_logged_without_a_notice()
    {
        var response = CreateHandler().Handle(new InvalidOperationException(), CrashSource.Process);

        response.ShouldBe(CrashResponse.CloseApp);
        var entry = _log.Entries.ShouldHaveSingleItem();
        entry.Activity.ShouldBe(ActivityEvent.AppCrashed);
        entry.Details.Source.ShouldBe(CrashSource.Process);
        _notifier.Shown.ShouldBeEmpty();
    }

    [Fact]
    public void An_aggregate_exception_is_logged_by_the_type_of_its_first_inner_exception()
    {
        _ = CreateHandler().Handle(new AggregateException(new FormatException()), CrashSource.BackgroundTask);

        _log.Entries.ShouldHaveSingleItem().Details.ExceptionType.ShouldBe(typeof(FormatException));
    }

    [Fact]
    public void The_exception_message_never_reaches_the_log_or_the_notice()
    {
        _ = CreateHandler().Handle(new InvalidOperationException("SECRET-CLIP-TEXT"), CrashSource.UiThread);

        var details = _log.Entries.ShouldHaveSingleItem().Details;
        details.ToString().ShouldNotContain("SECRET-CLIP-TEXT");
        _notifier.Shown.ShouldAllBe(shown => !shown.Message.Contains("SECRET-CLIP-TEXT", StringComparison.Ordinal));
    }

    [Fact]
    public void Handle_survives_a_failing_log_and_notifier()
    {
        var handler = new CrashHandler(new ThrowingLog(), new ThrowingNotifier());

        handler.Handle(new InvalidOperationException(), CrashSource.UiThread).ShouldBe(CrashResponse.CloseApp);
        handler.Handle(new InvalidOperationException(), CrashSource.BackgroundTask).ShouldBe(CrashResponse.KeepRunning);
    }

    [Fact]
    public void A_second_crash_while_closing_does_not_notify_again()
    {
        var handler = CreateHandler();

        _ = handler.Handle(new InvalidOperationException(), CrashSource.UiThread);
        var second = handler.Handle(new ArgumentException(), CrashSource.UiThread);

        second.ShouldBe(CrashResponse.CloseApp);
        _notifier.Shown.Count.ShouldBe(1);
        _log.Entries.Count.ShouldBe(2);
    }

    [Fact]
    public void A_null_exception_is_a_bug_in_the_caller()
    {
        _ = Should.Throw<ArgumentNullException>(() => CreateHandler().Handle(null!, CrashSource.UiThread));
    }

    private sealed class ThrowingLog : IActivityLog
    {
        public void Write(ActivityLevel level, ActivityEvent activity, ActivityDetails details = default) =>
            throw new IOException("disk full");
    }

    private sealed class ThrowingNotifier : IUserNotifier
    {
        public void Show(string title, string message) => throw new InvalidOperationException("no tray");
    }
}
