using System.Security.Cryptography;
using System.Text;
using ClipBench.Core.Results;
using ClipBench.Storage.Crypto;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests.Crypto;

public sealed class AesGcmCryptoServiceTests : IDisposable
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly byte[] _aad = Encoding.UTF8.GetBytes("row-1/content");
    private readonly AesGcmCryptoService _crypto = new(TestKeys.Master(1));

    public void Dispose() => _crypto.Dispose();

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    // --- encrypt and decrypt ---

    [Fact]
    public void A_sealed_value_opens_to_the_original_bytes()
    {
        var sealedValue = _crypto.Encrypt(Bytes("hello clipboard"), _aad);

        var opened = _crypto.Decrypt(sealedValue, _aad);

        opened.IsSuccess.ShouldBeTrue();
        opened.Value.ShouldBe(Bytes("hello clipboard"));
    }

    [Fact]
    public void An_empty_value_round_trips()
    {
        var sealedValue = _crypto.Encrypt([], _aad);

        _crypto.Decrypt(sealedValue, _aad).Value.ShouldBeEmpty();
    }

    [Fact]
    public void A_value_with_no_associated_data_round_trips()
    {
        var sealedValue = _crypto.Encrypt(Bytes("bare"), []);

        _crypto.Decrypt(sealedValue, []).Value.ShouldBe(Bytes("bare"));
    }

    [Fact]
    public void The_sealed_layout_is_nonce_then_ciphertext_then_tag()
    {
        var plaintext = Bytes("layout");

        var sealedValue = _crypto.Encrypt(plaintext, _aad);

        sealedValue.Length.ShouldBe(NonceSize + plaintext.Length + TagSize);
    }

    [Fact]
    public void The_sealed_value_does_not_contain_the_plaintext()
    {
        var marker = Bytes("a-distinctive-marker-0123456789");

        var sealedValue = _crypto.Encrypt(marker, _aad);

        sealedValue.AsSpan().IndexOf(marker).ShouldBe(-1);
    }

    [Fact]
    public void Sealing_the_same_value_twice_gives_different_results()
    {
        var first = _crypto.Encrypt(Bytes("same"), _aad);
        var second = _crypto.Encrypt(Bytes("same"), _aad);

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Ten_thousand_nonces_are_all_different()
    {
        var nonces = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 10_000; i++)
        {
            var sealedValue = _crypto.Encrypt(Bytes("x"), _aad);
            nonces.Add(Convert.ToHexString(sealedValue.AsSpan(0, NonceSize)));
        }

        nonces.Count.ShouldBe(10_000);
    }

    [Fact]
    public void The_caller_can_change_its_key_array_after_construction_without_effect()
    {
        var key = TestKeys.Master(9);
        using var crypto = new AesGcmCryptoService(key);
        var sealedValue = crypto.Encrypt(Bytes("kept"), _aad);

        Array.Clear(key);

        crypto.Decrypt(sealedValue, _aad).Value.ShouldBe(Bytes("kept"));
    }

    // --- tampering and wrong bindings ---

    [Fact]
    public void Changing_any_bit_of_any_byte_of_a_sealed_value_fails_authentication()
    {
        var sealedValue = _crypto.Encrypt(Bytes("do not touch me"), _aad);

        for (var index = 0; index < sealedValue.Length; index++)
        {
            for (var bit = 0; bit < 8; bit++)
            {
                var tampered = (byte[])sealedValue.Clone();
                tampered[index] ^= (byte)(1 << bit);

                var opened = _crypto.Decrypt(tampered, _aad);

                opened.IsFailure.ShouldBeTrue($"byte {index}, bit {bit} was accepted");
                opened.Error.Code.ShouldBe(ErrorCode.DecryptionFailed);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(NonceSize + TagSize - 1)]
    public void A_value_too_short_to_hold_a_nonce_and_a_tag_fails(int length)
    {
        var opened = _crypto.Decrypt(new byte[length], _aad);

        opened.IsFailure.ShouldBeTrue();
        opened.Error.Code.ShouldBe(ErrorCode.DecryptionFailed);
    }

    [Fact]
    public void A_truncated_or_extended_value_fails()
    {
        var sealedValue = _crypto.Encrypt(Bytes("exact length"), _aad);

        _crypto.Decrypt(sealedValue.AsSpan(0, sealedValue.Length - 1), _aad).IsFailure.ShouldBeTrue();
        _crypto.Decrypt([.. sealedValue, 0], _aad).IsFailure.ShouldBeTrue();
        _crypto.Decrypt(sealedValue.AsSpan(1), _aad).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_different_row_in_the_associated_data_fails()
    {
        var sealedValue = _crypto.Encrypt(Bytes("bound to row 1"), Bytes("row-1/content"));

        var opened = _crypto.Decrypt(sealedValue, Bytes("row-2/content"));

        opened.IsFailure.ShouldBeTrue();
        opened.Error.Code.ShouldBe(ErrorCode.DecryptionFailed);
    }

    [Fact]
    public void A_different_column_in_the_associated_data_fails()
    {
        var sealedValue = _crypto.Encrypt(Bytes("bound to content"), Bytes("row-1/content"));

        _crypto.Decrypt(sealedValue, Bytes("row-1/preview")).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Missing_associated_data_fails()
    {
        var sealedValue = _crypto.Encrypt(Bytes("needs its binding"), _aad);

        _crypto.Decrypt(sealedValue, []).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_value_sealed_under_another_key_fails()
    {
        using var other = new AesGcmCryptoService(TestKeys.Master(2));
        var sealedValue = other.Encrypt(Bytes("someone else's"), _aad);

        var opened = _crypto.Decrypt(sealedValue, _aad);

        opened.IsFailure.ShouldBeTrue();
        opened.Error.Code.ShouldBe(ErrorCode.DecryptionFailed);
    }

    [Fact]
    public void A_failure_message_never_contains_the_data()
    {
        var sealedValue = _crypto.Encrypt(Bytes("secret-marker-value"), _aad);
        sealedValue[^1] ^= 1;

        var opened = _crypto.Decrypt(sealedValue, _aad);

        opened.Error.Message.ShouldNotContain("secret-marker-value");
        opened.ToString().ShouldNotContain("secret-marker-value");
    }

    // --- the keyed content hash ---

    [Fact]
    public void The_content_hash_is_32_bytes_and_stable_for_the_same_key_and_data()
    {
        var first = _crypto.ComputeContentHash(Bytes("duplicate me"));
        var second = _crypto.ComputeContentHash(Bytes("duplicate me"));

        first.Length.ShouldBe(32);
        first.ShouldBe(second);
    }

    [Fact]
    public void Different_content_gives_a_different_hash()
    {
        _crypto.ComputeContentHash(Bytes("one")).ShouldNotBe(_crypto.ComputeContentHash(Bytes("two")));
    }

    [Fact]
    public void The_same_content_under_a_different_master_key_gives_a_different_hash()
    {
        using var other = new AesGcmCryptoService(TestKeys.Master(2));

        other.ComputeContentHash(Bytes("same text")).ShouldNotBe(_crypto.ComputeContentHash(Bytes("same text")));
    }

    [Fact]
    public void The_content_hash_is_not_a_plain_hash_of_the_content()
    {
        // A plain SHA-256 would let anyone confirm a guess about a clip from the database alone.
        var data = Bytes("guess me");

        _crypto.ComputeContentHash(data).ShouldNotBe(SHA256.HashData(data));
    }

    [Fact]
    public void The_hash_key_is_derived_and_is_not_the_master_key_itself()
    {
        var master = TestKeys.Master(1);
        var data = Bytes("domain separation");

        _crypto.ComputeContentHash(data).ShouldNotBe(HMACSHA256.HashData(master, data));
    }

    [Fact]
    public void Hashing_empty_content_works()
    {
        _crypto.ComputeContentHash([]).Length.ShouldBe(32);
    }

    // --- the stored format is pinned: data written today must open after an update ---

    [Fact]
    public void The_hash_is_HMAC_SHA256_under_the_key_derived_for_hashing()
    {
        var hashKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, TestKeys.Master(1), 32, salt: null, info: Bytes("ClipBench v1 content hash"));
        var data = Bytes("pinned");

        _crypto.ComputeContentHash(data).ShouldBe(HMACSHA256.HashData(hashKey, data));
    }

    [Fact]
    public void A_sealed_value_opens_with_plain_AES_GCM_under_the_key_derived_for_encryption()
    {
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, TestKeys.Master(1), 32, salt: null, info: Bytes("ClipBench v1 value encryption"));
        var plaintext = Bytes("pinned");
        var sealedValue = _crypto.Encrypt(plaintext, _aad);
        var opened = new byte[plaintext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(
            sealedValue.AsSpan(0, NonceSize),
            sealedValue.AsSpan(NonceSize, plaintext.Length),
            sealedValue.AsSpan(NonceSize + plaintext.Length, TagSize),
            opened,
            _aad);

        opened.ShouldBe(plaintext);
    }

    // --- construction, limits and disposal ---

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public void A_master_key_that_is_not_32_bytes_is_a_bug(int length)
    {
        Should.Throw<ArgumentException>(() => new AesGcmCryptoService(new byte[length]));
    }

    [Fact]
    public void A_plaintext_over_the_cap_is_a_bug()
    {
        using var capped = new AesGcmCryptoService(TestKeys.Master(1), maxPlaintextBytes: 16);

        capped.Encrypt(new byte[16], _aad).ShouldNotBeEmpty();
        Should.Throw<ArgumentOutOfRangeException>(() => capped.Encrypt(new byte[17], _aad));
    }

    [Fact]
    public void A_sealed_value_over_the_cap_fails_without_being_processed()
    {
        using var small = new AesGcmCryptoService(TestKeys.Master(1), maxPlaintextBytes: 16);
        var sealedValue = _crypto.Encrypt(new byte[64], _aad);

        var opened = small.Decrypt(sealedValue, _aad);

        opened.IsFailure.ShouldBeTrue();
        opened.Error.Code.ShouldBe(ErrorCode.DecryptionFailed);
    }

    [Fact]
    public void A_cap_below_one_is_a_bug()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new AesGcmCryptoService(TestKeys.Master(1), maxPlaintextBytes: -1));
    }

    [Fact]
    public void A_disposed_service_refuses_to_work()
    {
        var crypto = new AesGcmCryptoService(TestKeys.Master(1));
        crypto.Dispose();

        Should.Throw<ObjectDisposedException>(() => crypto.Encrypt(Bytes("x"), _aad));
        Should.Throw<ObjectDisposedException>(() => crypto.Decrypt(new byte[40], _aad));
        Should.Throw<ObjectDisposedException>(() => crypto.ComputeContentHash(Bytes("x")));
        crypto.Dispose(); // disposing twice is allowed
    }
}
