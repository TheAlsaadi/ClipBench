using ClipBench.Storage.Keys;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests.Keys;

public class MasterKeyTests
{
    [Fact]
    public void A_generated_key_is_32_bytes_and_not_all_zero()
    {
        using var key = MasterKey.Generate();

        key.Bytes.Length.ShouldBe(MasterKey.Size);
        key.Bytes.ToArray().ShouldContain(b => b != 0);
    }

    [Fact]
    public void Two_generated_keys_differ()
    {
        using var first = MasterKey.Generate();
        using var second = MasterKey.Generate();

        first.Bytes.SequenceEqual(second.Bytes).ShouldBeFalse();
    }

    [Fact]
    public void A_key_built_from_bytes_holds_a_copy()
    {
        var bytes = TestKeys.Master(3);
        using var key = new MasterKey(bytes);

        Array.Clear(bytes);

        key.Bytes.SequenceEqual(TestKeys.Master(3)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void A_key_that_is_not_32_bytes_is_a_bug(int length)
    {
        Should.Throw<ArgumentException>(() => new MasterKey(new byte[length]));
    }

    [Fact]
    public void Disposing_a_key_wipes_it_and_refuses_further_reads()
    {
        var key = new MasterKey(TestKeys.Master(4));

        key.Dispose();

        Should.Throw<ObjectDisposedException>(() => _ = key.Bytes.Length);
        key.Dispose(); // twice is fine
    }

    [Fact]
    public void Printing_a_key_never_shows_its_bytes()
    {
        using var key = new MasterKey(TestKeys.Master(5));

        key.ToString().ShouldBe("MasterKey");
    }
}
