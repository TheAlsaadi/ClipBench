using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Principal;
using ClipBench.Platform.Windows.Hosting;

namespace ClipBench.Platform.Windows.IntegrationTests.Hosting;

// Named mutexes and events are kernel objects of the user's session and touch no user data, so these run on CI too.
public sealed class SingleInstanceGuardTests : IDisposable
{
    private readonly string _name = "ClipBenchTest" + Guid.NewGuid().ToString("N");
    private readonly List<SingleInstanceGuard> _guards = [];
    private readonly BlockingCollection<int> _requests = [];

    public void Dispose()
    {
        _guards.ForEach(guard => guard.Dispose());
        _requests.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SingleInstanceGuard Acquire()
    {
        var guard = SingleInstanceGuard.Acquire(_name);
        _guards.Add(guard);
        return guard;
    }

    private bool WaitForRequest() => _requests.TryTake(out _, 10_000, Ct);

    private bool NoRequest() => !_requests.TryTake(out _, 300, Ct);

    [Fact]
    public void The_first_to_ask_is_the_running_copy_and_the_second_is_not()
    {
        var first = Acquire();
        var second = Acquire();

        first.IsFirstInstance.ShouldBeTrue();
        second.IsFirstInstance.ShouldBeFalse();
    }

    [Fact]
    public void Once_the_running_copy_has_gone_the_next_to_ask_is_the_running_copy()
    {
        var first = Acquire();
        first.Dispose();

        Acquire().IsFirstInstance.ShouldBeTrue();
    }

    [Fact]
    public void Different_names_do_not_get_in_each_others_way()
    {
        var one = Acquire();
        using var other = SingleInstanceGuard.Acquire(_name + "Other");

        one.IsFirstInstance.ShouldBeTrue();
        other.IsFirstInstance.ShouldBeTrue();
    }

    [Fact]
    public void A_second_launch_asks_the_running_copy_to_open_the_popup_off_the_callers_thread()
    {
        var first = Acquire();
        int? thread = null;
        first.ShowRequested += (_, _) =>
        {
            thread = Environment.CurrentManagedThreadId;
            _requests.Add(1);
        };
        var second = Acquire();

        second.SignalFirstInstance().ShouldBeTrue();

        WaitForRequest().ShouldBeTrue();
        thread.ShouldNotBe(Environment.CurrentManagedThreadId);
        NoRequest().ShouldBeTrue("one signal is one request");
    }

    [Fact]
    public void A_request_made_before_anyone_listens_is_kept_for_the_first_listener()
    {
        // A second launch can arrive while the running copy is still setting itself up.
        var first = Acquire();
        var second = Acquire();
        second.SignalFirstInstance().ShouldBeTrue();

        // Subscribing at once would usually beat the signal to the guard. Wait until the guard has seen the signal
        // with nobody listening, so the request really is a waiting one.
        SpinWait.SpinUntil(() => first.HasPendingRequest, TimeSpan.FromSeconds(10)).ShouldBeTrue("the guard should hold the request");
        _requests.Count.ShouldBe(0);

        first.ShowRequested += (_, _) => _requests.Add(1);

        WaitForRequest().ShouldBeTrue();
        first.HasPendingRequest.ShouldBeFalse("the request was handed over");
    }

    [Fact]
    public void Each_separate_signal_is_a_separate_request()
    {
        var first = Acquire();
        first.ShowRequested += (_, _) => _requests.Add(1);
        var second = Acquire();

        second.SignalFirstInstance();
        WaitForRequest().ShouldBeTrue();
        second.SignalFirstInstance();
        WaitForRequest().ShouldBeTrue();
    }

    [Fact]
    public void A_listener_that_throws_does_not_stop_the_next_request()
    {
        var first = Acquire();
        first.ShowRequested += (_, _) => throw new InvalidOperationException("a subscriber bug");
        first.ShowRequested += (_, _) => _requests.Add(1);
        var second = Acquire();

        second.SignalFirstInstance();
        WaitForRequest().ShouldBeTrue();
        second.SignalFirstInstance();
        WaitForRequest().ShouldBeTrue();
    }

    [Fact]
    public void A_running_copy_this_user_may_not_open_counts_as_another_copy_and_does_not_crash_the_launch()
    {
        // A copy started with Run as administrator creates its objects with a higher integrity label, and an ordinary copy
        // is refused access to them. Simulated with a mutex and an event that refuse everyone but their creator.
        var world = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var mutexSecurity = new MutexSecurity();
        mutexSecurity.AddAccessRule(new MutexAccessRule(world, MutexRights.FullControl, AccessControlType.Deny));
        using var elevatedMutex = MutexAcl.Create(false, $@"Local\{_name}.Instance", out var created, mutexSecurity);
        var eventSecurity = new EventWaitHandleSecurity();
        eventSecurity.AddAccessRule(new EventWaitHandleAccessRule(world, EventWaitHandleRights.FullControl, AccessControlType.Deny));
        using var elevatedEvent = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, $@"Local\{_name}.ShowPopup", out _, eventSecurity);
        created.ShouldBeTrue();

        var guard = Should.NotThrow(Acquire);

        guard.IsFirstInstance.ShouldBeFalse();
        guard.SignalFirstInstance().ShouldBeFalse("there is no way to reach a copy that refuses us");
    }

    [Fact]
    public void A_running_copy_whose_event_this_user_may_not_open_cannot_be_signalled_but_the_launch_still_works()
    {
        var first = Acquire();
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), EventWaitHandleRights.FullControl, AccessControlType.Deny));
        using var locked = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, $@"Local\{_name}Other.ShowPopup", out var created, security);
        created.ShouldBeTrue();
        using var other = SingleInstanceGuard.Acquire(_name + "Other");

        other.IsFirstInstance.ShouldBeTrue("the mutex was free");
        first.IsFirstInstance.ShouldBeTrue();
    }
    [Fact]
    public void The_running_copy_has_nobody_to_signal()
    {
        Acquire().SignalFirstInstance().ShouldBeFalse();
    }

    [Fact]
    public void Signalling_after_dispose_reaches_nobody()
    {
        var first = Acquire();
        var second = Acquire();
        second.Dispose();

        second.SignalFirstInstance().ShouldBeFalse();
    }

    [Fact]
    public void Disposing_twice_is_fine()
    {
        var guard = Acquire();

        guard.Dispose();

        Should.NotThrow(guard.Dispose);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("with space")]
    [InlineData("back\\slash")]
    [InlineData("Local\\Other")]
    [InlineData("slash/name")]
    [InlineData("star*")]
    public void A_name_with_anything_but_letters_digits_dots_dashes_and_underscores_is_a_bug(string name)
    {
        Should.Throw<ArgumentException>(() => SingleInstanceGuard.Acquire(name));
    }

    [Fact]
    public void A_name_that_is_too_long_is_a_bug()
    {
        Should.Throw<ArgumentException>(() => SingleInstanceGuard.Acquire(new string('a', 101)));
    }

    [Fact]
    public void The_app_uses_one_fixed_name_for_the_signed_in_user()
    {
        using var guard = SingleInstanceGuard.ForCurrentUser();

        // Whether this is the first copy depends on whether ClipBench itself is running; the name must not.
        SingleInstanceGuard.AppName.ShouldBe("ClipBench");
        guard.ShouldNotBeNull();
    }
}
