using System.Text;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Security;

namespace ClipBench.Platform.Windows.IntegrationTests.Security;

// DPAPI touches no clipboard and no user data, so these run everywhere Windows does, including CI.
public class DpapiKeyProtectorTests
{
    // A fixed 32-byte stand-in for a master key. Real keys are random; here the value must be known.
    private static readonly byte[] _secret = [.. Enumerable.Range(1, 32).Select(i => (byte)(i * 7))];

    private static DpapiKeyProtector Create(string entropy = "test entropy one") => new(Encoding.UTF8.GetBytes(entropy));

    [Fact]
    public void A_wrapped_secret_unwraps_to_the_same_bytes()
    {
        var protector = Create();

        var wrapped = protector.Protect(_secret);
        var unwrapped = protector.Unprotect(wrapped.Value);

        wrapped.IsSuccess.ShouldBeTrue();
        unwrapped.IsSuccess.ShouldBeTrue();
        unwrapped.Value.ShouldBe(_secret);
    }

    [Fact]
    public void A_second_protector_with_the_same_entropy_unwraps_what_the_first_wrapped()
    {
        // The app restarts between writing the key file and reading it, so nothing may live in the instance.
        var wrapped = Create().Protect(_secret).Value;

        var unwrapped = Create().Unprotect(wrapped);

        unwrapped.IsSuccess.ShouldBeTrue();
        unwrapped.Value.ShouldBe(_secret);
    }

    [Fact]
    public void The_wrapped_form_does_not_contain_the_secret()
    {
        var wrapped = Create().Protect(_secret).Value;

        wrapped.AsSpan().IndexOf(_secret).ShouldBe(-1);
        wrapped.Length.ShouldBeGreaterThan(_secret.Length);
    }

    [Fact]
    public void Wrapping_the_same_secret_twice_gives_different_bytes()
    {
        var protector = Create();

        var first = protector.Protect(_secret).Value;
        var second = protector.Protect(_secret).Value;

        first.ShouldNotBe(second);
    }

    [Fact]
    public void A_protector_with_different_entropy_cannot_unwrap()
    {
        var wrapped = Create("test entropy one").Protect(_secret).Value;

        var result = Create("test entropy two").Unprotect(wrapped);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.KeyProtectionFailed);
    }

    [Fact]
    public void Changing_any_byte_of_the_wrap_makes_it_fail_or_leaves_the_secret_unchanged()
    {
        var protector = Create();
        var wrapped = protector.Protect(_secret).Value;
        var unchangedSecret = 0;

        for (var i = 0; i < wrapped.Length; i++)
        {
            var damaged = (byte[])wrapped.Clone();
            damaged[i] ^= 0x01;

            var result = protector.Unprotect(damaged);

            if (result.IsSuccess)
            {
                // Only bytes that DPAPI does not use may change without effect; the secret must never differ.
                result.Value.ShouldBe(_secret, $"byte {i} changed the secret instead of failing");
                unchangedSecret++;
            }
            else
            {
                result.Error.Code.ShouldBe(ErrorCode.KeyProtectionFailed);
            }
        }

        // Nearly every byte is covered by DPAPI's integrity check. On the machine this was written on, 16 of 262
        // bytes (a header field) can change without effect. The bound catches a wrap that stops being checked.
        unchangedSecret.ShouldBeLessThan(wrapped.Length / 8, "most bytes of the wrap must be authenticated");
    }

    [Fact]
    public void A_failure_carries_the_windows_error_number_so_it_can_be_diagnosed()
    {
        // 0 would mean the last-error value was not captured (the declaration lost SetLastError).
        var result = Create().Unprotect(new byte[100]);

        result.Error.Message.ShouldContain("(error ");
        result.Error.Message.ShouldNotContain("(error 0)");
    }

    [Fact]
    public void A_truncated_wrap_fails()
    {
        var protector = Create();
        var wrapped = protector.Protect(_secret).Value;

        var result = protector.Unprotect(wrapped.AsSpan(0, wrapped.Length - 1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.KeyProtectionFailed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(5000)]
    public void Bytes_that_are_not_a_wrap_fail(int length)
    {
        var junk = new byte[length];
        new Random(length).NextBytes(junk);

        var result = Create().Unprotect(junk);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.KeyProtectionFailed);
    }

    [Fact]
    public void Nothing_to_protect_is_a_failure_and_not_an_empty_wrap()
    {
        var result = Create().Protect([]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.KeyProtectionFailed);
    }

    [Fact]
    public void A_secret_over_the_size_cap_is_refused_without_calling_Windows()
    {
        var result = Create().Protect(new byte[DpapiKeyProtector.MaxSecretBytes + 1]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.KeyProtectionFailed);
    }

    [Fact]
    public void A_wrap_over_the_size_cap_is_refused_without_calling_Windows()
    {
        var result = Create().Unprotect(new byte[DpapiKeyProtector.MaxWrapBytes + 1]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.KeyProtectionFailed);
    }

    [Fact]
    public void A_secret_at_the_size_cap_still_round_trips()
    {
        var protector = Create();
        var biggest = new byte[DpapiKeyProtector.MaxSecretBytes];
        new Random(7).NextBytes(biggest);

        var wrapped = protector.Protect(biggest);

        wrapped.IsSuccess.ShouldBeTrue();
        wrapped.Value.Length.ShouldBeLessThanOrEqualTo(DpapiKeyProtector.MaxWrapBytes);
        protector.Unprotect(wrapped.Value).Value.ShouldBe(biggest);
    }

    [Fact]
    public void Entropy_is_required_because_it_is_what_ties_a_wrap_to_this_app()
    {
        Should.Throw<ArgumentException>(() => new DpapiKeyProtector([]));
    }

    [Fact]
    public void The_protector_the_app_uses_unwraps_its_own_wraps_and_not_those_of_other_entropy()
    {
        var wrapped = DpapiKeyProtector.ForClipBench().Protect(_secret).Value;

        DpapiKeyProtector.ForClipBench().Unprotect(wrapped).Value.ShouldBe(_secret);
        Create().Unprotect(wrapped).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_failure_message_does_not_contain_the_secret_or_the_wrap()
    {
        var wrapped = Create("test entropy one").Protect(_secret).Value;

        var result = Create("test entropy two").Unprotect(wrapped);

        result.Error.Message.ShouldNotContain(Convert.ToHexString(_secret), Case.Insensitive);
        result.Error.Message.ShouldNotContain(Convert.ToBase64String(wrapped));
    }
}
