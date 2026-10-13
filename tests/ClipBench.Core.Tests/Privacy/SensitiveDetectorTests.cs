using System.Globalization;
using ClipBench.Core.Privacy;

namespace ClipBench.Core.Tests.Privacy;

public class SensitiveDetectorTests
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private static SensitiveKind Detect(string text) => SensitiveDetector.Detect(text);

    /// <summary>Appends the Luhn check digit to a digit prefix, so a test can build a number that is valid by construction.</summary>
    private static string WithLuhnCheckDigit(string prefix)
    {
        var sum = 0;
        for (var i = 0; i < prefix.Length; i++)
        {
            var digit = prefix[prefix.Length - 1 - i] - '0';
            if (i % 2 == 0)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return prefix + ((10 - (sum % 10)) % 10);
    }

    /// <summary>A deterministic random-looking token: seeded so the test never changes between runs.</summary>
    private static string RandomToken(int length, int seed = 7)
    {
        var random = new Random(seed);
        return string.Create(length, random, static (span, rng) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = Alphabet[rng.Next(Alphabet.Length)];
            }
        });
    }

    // --- normal text is left alone ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Hello, world!")]
    [InlineData("Meeting moved to 3pm on 2026-10-13. Bring the quarterly report, and the budget for Q4.")]
    [InlineData("The quick brown fox jumps over the lazy dog. It was the best of times, it was the worst of times.")]
    [InlineData("مرحبا بالعالم، هذا نص عادي لا يحتوي على أسرار")]
    [InlineData("const x = items.filter(i => i.active).map(i => i.id); // returns the ids")]
    [InlineData("Call me on +1 (555) 123-4567 or +44 20 7946 0958 after lunch.")]
    [InlineData("Order #1234567890123 shipped on 1700000000000 (epoch milliseconds).")]
    public void Ordinary_text_is_not_flagged(string text)
    {
        Detect(text).ShouldBe(SensitiveKind.None);
    }

    [Fact]
    public void A_missing_text_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => SensitiveDetector.Detect(null!));
    }

    [Fact]
    public void Malformed_text_does_not_crash_the_detector()
    {
        Detect("ab\uD83Dcd 4111 1111 1111 1111 \uDE00").ShouldBe(SensitiveKind.CardNumber);
        Detect("\uD83D").ShouldBe(SensitiveKind.None);
    }

    // --- card numbers ---

    [Theory]
    [InlineData("4111 1111 1111 1111")]     // Visa
    [InlineData("4111111111111111")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("4222 2222 2222 2")]        // Visa, 13 digits
    [InlineData("5555 5555 5555 4444")]     // Mastercard
    [InlineData("2223 0031 2200 3222")]     // Mastercard, new range
    [InlineData("3782 822463 10005")]       // American Express, 4-6-5
    [InlineData("3714 496353 98431")]
    [InlineData("6011 1111 1111 1117")]     // Discover
    [InlineData("3530 1113 3330 0000")]     // JCB
    [InlineData("3056 9309 0259 04")]       // Diners Club, 14 digits
    [InlineData("6200 0000 0000 0005")]     // UnionPay
    public void A_card_number_that_passes_the_Luhn_check_is_flagged(string card)
    {
        Detect(card).ShouldBe(SensitiveKind.CardNumber);
    }

    [Theory]
    [InlineData("my card is 4111 1111 1111 1111 thanks")]
    [InlineData("card:4111111111111111.")]
    [InlineData("(4111111111111111)")]
    [InlineData("Number\r\n4111 1111 1111 1111\r\nExpires 12/29")]
    [InlineData("Tel 12 4111 1111 1111 1111")]
    public void A_card_number_is_found_inside_other_text(string text)
    {
        Detect(text).ShouldBe(SensitiveKind.CardNumber);
    }

    [Fact]
    public void A_card_number_written_with_arabic_indic_digits_is_flagged()
    {
        const string card = "٤١١١ ١١١١ ١١١١ ١١١١";

        Detect(card).ShouldBe(SensitiveKind.CardNumber);
    }

    [Theory]
    [InlineData("4111 1111 1111 1112")]     // fails the Luhn check
    [InlineData("4111111111111112")]
    [InlineData("5555 5555 5555 4445")]
    [InlineData("3782 822463 10006")]
    public void A_card_number_that_fails_the_Luhn_check_is_not_flagged(string card)
    {
        Detect(card).ShouldBe(SensitiveKind.None);
    }

    [Fact]
    public void A_number_that_passes_the_Luhn_check_but_belongs_to_no_card_brand_is_not_flagged()
    {
        // Long ids, order numbers and timestamps pass the check one time in ten.
        foreach (var number in new[] { WithLuhnCheckDigit("123456789012345"), WithLuhnCheckDigit("100000000000000"), WithLuhnCheckDigit("700000000000000") })
        {
            number.Length.ShouldBe(16);
            Detect(number).ShouldBe(SensitiveKind.None);
        }
    }

    [Theory]
    [InlineData("411111111111")]            // 12 digits: too short
    [InlineData("41111111111111111111")]    // 20 digits: too long
    public void Digit_runs_of_the_wrong_length_are_not_flagged(string digits)
    {
        Detect(digits).ShouldBe(SensitiveKind.None);
    }

    [Theory]
    [InlineData("41 11 11 11 11 11 11 11")]     // two-digit groups
    [InlineData("4111  1111  1111  1111")]      // double spaces
    [InlineData("4111 11111 1111 1111")]        // uneven groups
    [InlineData("4111_1111_1111_1111")]
    public void Digits_that_are_not_grouped_like_a_card_are_not_flagged(string text)
    {
        Detect(text).ShouldBe(SensitiveKind.None);
    }

    [Theory]
    [InlineData("ID4111111111111111")]          // glued to letters
    [InlineData("4111111111111111x")]
    [InlineData("a4111 1111 1111 1111")]
    public void Digits_glued_to_letters_are_not_a_card_number(string text)
    {
        Detect(text).ShouldBe(SensitiveKind.None);
    }

    [Fact]
    public void A_list_of_two_digit_numbers_is_not_a_card_number()
    {
        Detect("10 20 30 40 50 60 70 80").ShouldBe(SensitiveKind.None);
        Detect("42 43 44 45 46 47 48 49").ShouldBe(SensitiveKind.None);
    }

    // --- IBAN ---

    [Theory]
    [InlineData("DE89 3704 0044 0532 0130 00")]
    [InlineData("DE89370400440532013000")]
    [InlineData("GB82 WEST 1234 5698 7654 32")]
    [InlineData("FR76 3000 6000 0112 3456 7890 189")]
    [InlineData("BE68 5390 0754 7034")]
    [InlineData("NL91 ABNA 0417 1643 00")]
    [InlineData("de89 3704 0044 0532 0130 00")]
    [InlineData("SA03 8000 0000 6080 1016 7519")]
    public void An_IBAN_with_a_correct_check_is_flagged(string iban)
    {
        Detect(iban).ShouldBe(SensitiveKind.Iban);
    }

    [Theory]
    [InlineData("IBAN: DE89 3704 0044 0532 0130 00.")]
    [InlineData("Please pay DE89 3704 0044 0532 0130 00 before Friday")]
    [InlineData("Please pay BE68 5390 0754 7034 today")]
    [InlineData("to:DE89370400440532013000,")]
    public void An_IBAN_is_found_inside_other_text(string text)
    {
        Detect(text).ShouldBe(SensitiveKind.Iban);
    }

    [Theory]
    [InlineData("DE89 3704 0044 0532 0130 01")]     // wrong check
    [InlineData("DE00 3704 0044 0532 0130 00")]
    [InlineData("GB82 WEST 1234 5698 7654 33")]
    [InlineData("DE89 3704")]                       // too short
    [InlineData("DE89 37045 0044 0532 0130 00")]    // a group of five
    [InlineData("DE89370400440532013000123456789012345")]   // 37 characters: longer than any IBAN
    [InlineData("XX12 3456 7890 1234 5678 9012 3456 7890 1234 5678")]   // ten groups: longer than any IBAN
    [InlineData("AB12 CDEF GHIJ KLMN")]
    public void Something_that_only_looks_like_an_IBAN_is_not_flagged(string text)
    {
        Detect(text).ShouldBe(SensitiveKind.None);
    }

    // --- JWT ---

    private const string Jwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

    [Theory]
    [InlineData(Jwt)]
    [InlineData("Authorization: Bearer " + Jwt)]
    [InlineData("{\"token\":\"" + Jwt + "\"}")]
    [InlineData("curl -H 'Authorization: Bearer " + Jwt + "' https://example.com")]
    public void A_JSON_web_token_is_flagged(string text)
    {
        Detect(text).ShouldBe(SensitiveKind.Jwt);
    }

    [Theory]
    [InlineData("eyJhbGciOiJIUzI1NiJ9")]                     // a header alone
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJhIjoxfQ")]          // no signature
    [InlineData("eyJhbGciOiJIUzI1NiJ9 eyJzdWIiOiIxMjM0NTY3ODkwIn0")]  // parts separated by a space, not a dot
    [InlineData("see v1.2.3 and example.com.au")]
    public void Something_that_is_not_a_complete_token_is_not_flagged(string text)
    {
        Detect(text).ShouldBe(SensitiveKind.None);
    }

    [Fact]
    public void A_very_long_run_of_header_like_text_is_scanned_in_linear_time()
    {
        // Every "eyJ" sits inside one huge run of token characters. Rescanning the run for each one would take forever.
        var hostile = string.Concat(Enumerable.Repeat("eyJ", 300_000));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Detect(hostile);
        watch.Stop();

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    // --- private keys ---

    [Theory]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----")]
    [InlineData("-----BEGIN PRIVATE KEY-----")]
    [InlineData("-----BEGIN EC PRIVATE KEY-----")]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----")]
    [InlineData("-----BEGIN ENCRYPTED PRIVATE KEY-----")]
    [InlineData("-----BEGIN PGP PRIVATE KEY BLOCK-----")]
    [InlineData("-----begin rsa private key-----")]
    [InlineData("here you go:\r\n-----BEGIN RSA PRIVATE KEY-----\r\nMIIEowIBAAKCAQEA\r\n-----END RSA PRIVATE KEY-----\r\n")]
    [InlineData("-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n-----BEGIN PRIVATE KEY-----\nMIIE")]
    public void A_private_key_block_is_flagged(string text)
    {
        Detect(text).ShouldBe(SensitiveKind.PrivateKey);
    }

    [Theory]
    [InlineData("-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8A\n-----END PUBLIC KEY-----")]
    [InlineData("-----BEGIN CERTIFICATE-----\nMIIDdzCCAl+gAwIBAgIEAgAAuTANBgkqhkiG9w0BAQUFADBa\n-----END CERTIFICATE-----")]
    [InlineData("-----BEGIN CERTIFICATE REQUEST-----\nMIIB\n-----END CERTIFICATE REQUEST-----")]
    [InlineData("-----BEGIN PGP PUBLIC KEY BLOCK-----")]
    [InlineData("-----BEGIN PGP MESSAGE-----")]
    [InlineData("a private key is a secret; keep your private key safe")]
    public void Public_keys_certificates_and_talk_about_keys_are_not_flagged(string text)
    {
        Detect(text).ShouldBe(SensitiveKind.None);
    }

    [Fact]
    public void A_begin_line_with_no_end_of_header_is_not_flagged_and_does_not_hang()
    {
        Detect("-----BEGIN " + new string('x', 5000)).ShouldBe(SensitiveKind.None);
        Detect(string.Concat(Enumerable.Repeat("-----BEGIN ", 50_000))).ShouldBe(SensitiveKind.None);
    }

    // --- tokens ---

    [Theory]
    [InlineData("k3J9xQ2mT7vB5nZ8pL4wR6yH1cD0fGsA")]
    [InlineData("tok_R3hD9kLmP2xQ7vNb4TzY8wCjF5sA1eUo6G0i")]
    [InlineData("demo_live_Zt8KpQ2xV7mR4nB9cL3wY6hD1fGj5s")]
    [InlineData("Qx7vB2mK9pL4zT8wR1yH6cD3fGjN5sAa/eUo+0iV")]
    [InlineData("token = 'k3J9xQ2mT7vB5nZ8pL4wR6yH1cD0fGsA'")]
    [InlineData("API_KEY=k3J9xQ2mT7vB5nZ8pL4wR6yH1cD0fGsA\r\n")]
    public void A_long_random_looking_token_is_flagged(string text)
    {
        Detect(text).ShouldBe(SensitiveKind.Token);
    }

    [Fact]
    public void A_random_token_of_the_largest_size_is_flagged_and_one_longer_is_not()
    {
        Detect(RandomToken(256)).ShouldBe(SensitiveKind.Token);
        Detect(RandomToken(257)).ShouldBe(SensitiveKind.None);
    }

    [Fact]
    public void A_random_token_of_the_smallest_size_is_flagged_and_one_shorter_is_not()
    {
        Detect(RandomToken(32)).ShouldBe(SensitiveKind.Token);
        Detect(RandomToken(31)).ShouldBe(SensitiveKind.None);
    }

    [Theory]
    [InlineData("3b18e512dba79e4c8300dd08aeb37f8e728b8dad")]                                      // git commit
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]             // SHA-256
    [InlineData("d41d8cd98f00b204e9800998ecf8427e")]                                            // MD5
    [InlineData("123e4567-e89b-12d3-a456-426614174000")]                                         // UUID
    [InlineData("ConfigurationManagerServiceProviderFactory")]                                  // a class name
    [InlineData("InternationalizationLocalizationConfigurationManager")]
    [InlineData("pneumonoultramicroscopicsilicovolcanoconiosis")]
    [InlineData("this-is-a-very-long-article-slug-about-clipboard-managers-for-windows-2026")]
    [InlineData("usr/local/lib/python3/site-packages/numpy/core/tests/test_dtype")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1")]
    [InlineData("a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1")]
    [InlineData("12345678901234567890123456789012345678901234567890")]
    public void Hashes_ids_names_slugs_paths_and_repetitive_text_are_not_flagged(string text)
    {
        Detect(text).ShouldBe(SensitiveKind.None);
    }

    [Fact]
    public void A_commit_hash_inside_a_sentence_is_not_flagged()
    {
        Detect("fixed in 3b18e512dba79e4c8300dd08aeb37f8e728b8dad, see the PR").ShouldBe(SensitiveKind.None);
    }

    // --- card brands: the prefix and length ranges ---

    [Theory]
    [InlineData("510000000000000", true)]       // Mastercard 51
    [InlineData("550000000000000", true)]       // Mastercard 55
    [InlineData("500000000000000", false)]      // 50 is not Mastercard
    [InlineData("560000000000000", false)]      // 56 is not Mastercard
    [InlineData("222100000000000", true)]       // Mastercard 2221
    [InlineData("272000000000000", true)]       // Mastercard 2720
    [InlineData("222000000000000", false)]      // just below 2221
    [InlineData("272100000000000", false)]      // just above 2720
    [InlineData("601100000000000", true)]       // Discover 6011
    [InlineData("650000000000000", true)]       // Discover 65
    [InlineData("644000000000000", true)]       // Discover 644
    [InlineData("649000000000000", true)]       // Discover 649
    [InlineData("643000000000000", false)]      // just below 644
    [InlineData("660000000000000", false)]
    [InlineData("352800000000000", true)]       // JCB 3528
    [InlineData("358900000000000", true)]       // JCB 3589
    [InlineData("352700000000000", false)]
    [InlineData("359000000000000", false)]
    [InlineData("300000000000000", true)]       // Diners Club 300
    [InlineData("305000000000000", true)]       // Diners Club 305
    [InlineData("306000000000000", false)]
    [InlineData("360000000000000", true)]       // Diners Club 36
    [InlineData("380000000000000", true)]       // Diners Club 38
    [InlineData("390000000000000", true)]       // Diners Club 39
    [InlineData("370000000000000", false)]      // 37 is American Express, which has 15 digits, not 16
    [InlineData("620000000000000", true)]       // UnionPay 62
    [InlineData("630000000000000", false)]
    public void Only_numbers_with_a_known_brand_prefix_are_flagged(string prefixWithoutCheckDigit, bool isCard)
    {
        var number = WithLuhnCheckDigit(prefixWithoutCheckDigit);

        number.Length.ShouldBe(16);
        Detect(number).ShouldBe(isCard ? SensitiveKind.CardNumber : SensitiveKind.None);
    }

    [Theory]
    [InlineData("4", 13, true)]         // Visa
    [InlineData("4", 14, false)]
    [InlineData("4", 15, false)]
    [InlineData("4", 16, true)]
    [InlineData("4", 17, false)]
    [InlineData("4", 18, false)]
    [InlineData("4", 19, true)]
    [InlineData("34", 14, false)]       // American Express
    [InlineData("34", 15, true)]
    [InlineData("34", 16, false)]
    [InlineData("36", 13, false)]       // Diners Club
    [InlineData("36", 14, true)]
    [InlineData("6011", 15, false)]     // Discover
    [InlineData("6011", 16, true)]
    [InlineData("6011", 17, true)]
    [InlineData("6011", 18, true)]
    [InlineData("6011", 19, true)]
    public void A_brand_must_also_have_one_of_its_lengths(string prefix, int totalDigits, bool isCard)
    {
        // The prefix, zeros, and a Luhn check digit as the last of totalDigits digits.
        var number = WithLuhnCheckDigit(prefix + new string('0', totalDigits - 1 - prefix.Length));

        number.Length.ShouldBe(totalDigits);
        Detect(number).ShouldBe(isCard ? SensitiveKind.CardNumber : SensitiveKind.None);
    }

    [Fact]
    public void The_last_group_of_a_card_number_has_at_most_four_digits()
    {
        // A Discover number with 17 digits, written three ways.
        var digits = WithLuhnCheckDigit("6011000000000000");
        digits.Length.ShouldBe(17);

        Detect(digits).ShouldBe(SensitiveKind.CardNumber);
        Detect($"{digits[..4]} {digits[4..8]} {digits[8..12]} {digits[12..16]} {digits[16..]}").ShouldBe(SensitiveKind.CardNumber);
        Detect($"{digits[..4]} {digits[4..8]} {digits[8..12]} {digits[12..]}").ShouldBe(SensitiveKind.None);
    }

    // --- IBAN: the length and check digit limits ---

    /// <summary>The ISO 7064 remainder, computed independently of the detector so the test is a real cross-check.</summary>
    private static int Mod97(string iban)
    {
        var remainder = 0;
        foreach (var c in iban[4..] + iban[..4])
        {
            var value = char.IsDigit(c) ? c - '0' : char.ToUpperInvariant(c) - 'A' + 10;
            remainder = ((remainder * (value >= 10 ? 100 : 10)) + value) % 97;
        }

        return remainder;
    }

    /// <summary>An IBAN-shaped string of the given length with correctly calculated check digits.</summary>
    private static string ValidIban(int length)
    {
        var bban = string.Concat(Enumerable.Range(0, length - 4).Select(i => (char)('1' + (i % 9))));
        var check = 98 - Mod97("DE00" + bban);
        return "DE" + check.ToString("D2", CultureInfo.InvariantCulture) + bban;
    }

    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    [InlineData(33, true)]
    [InlineData(34, true)]
    [InlineData(35, false)]
    [InlineData(36, false)]
    public void An_IBAN_has_between_15_and_34_characters(int length, bool isIban)
    {
        var iban = ValidIban(length);

        Mod97(iban).ShouldBe(1, "the generator must produce a correct checksum for the test to mean anything");
        Detect(iban).ShouldBe(isIban ? SensitiveKind.Iban : SensitiveKind.None);
        Detect(string.Join(' ', Enumerable.Range(0, (length + 3) / 4).Select(i => iban.Substring(i * 4, Math.Min(4, length - (i * 4))))))
            .ShouldBe(isIban ? SensitiveKind.Iban : SensitiveKind.None);
    }

    [Theory]
    [InlineData("00")]
    [InlineData("01")]
    [InlineData("99")]
    public void Check_digits_of_00_01_and_99_are_never_valid_even_when_the_remainder_works_out(string checkDigits)
    {
        // The remainder test alone would accept these, because 00, 01 and 99 are congruent to valid values.
        var candidate = Enumerable.Range(0, 10_000)
            .Select(i => "DE" + checkDigits + "37040044053201" + i.ToString("D4", CultureInfo.InvariantCulture))
            .First(text => Mod97(text) == 1);

        Detect(candidate).ShouldBe(SensitiveKind.None);
    }

    // --- JWT: the minimum size of each part ---

    [Theory]
    [InlineData("eyJhbGciO", "eyJhIjoxfQ", "SflKxwRJSMeK", false)]      // header of 9 characters
    [InlineData("eyJhbGciOi", "eyJhIjoxfQ", "SflKxwRJSMeK", true)]      // header of 10 characters
    [InlineData("eyJhbGciOiJIUzI1NiJ9", "e", "SflKxwRJSMeK", false)]    // payload of 1 character
    [InlineData("eyJhbGciOiJIUzI1NiJ9", "e3", "SflKxwRJSMeK", true)]    // payload of 2 characters
    [InlineData("eyJhbGciOiJIUzI1NiJ9", "eyJhIjoxfQ", "", false)]       // no signature
    [InlineData("eyJhbGciOiJIUzI1NiJ9", "eyJhIjoxfQ", "SflKxwR", false)]    // signature of 7 characters
    [InlineData("eyJhbGciOiJIUzI1NiJ9", "eyJhIjoxfQ", "SflKxwRJ", true)]    // signature of 8 characters
    public void A_token_needs_a_header_a_payload_and_a_signature_of_a_sensible_size(string header, string payload, string signature, bool isJwt)
    {
        Detect($"{header}.{payload}.{signature}").ShouldBe(isJwt ? SensitiveKind.Jwt : SensitiveKind.None);
    }

    // --- tokens: slashes and the need for digits ---

    [Fact]
    public void A_token_may_have_a_few_slashes_but_a_path_may_not()
    {
        Detect("k3J9xQ2m/T7vB5nZ8/pL4wR6yH1/cD0fGsA").ShouldBe(SensitiveKind.Token);                 // three
        Detect("k3J9xQ2m/T7vB5nZ8/pL4wR6yH1/cD0fG/sA").ShouldBe(SensitiveKind.None);               // four
    }

    [Fact]
    public void A_long_string_of_letters_with_no_digit_is_a_word_not_a_token()
    {
        Detect("kJxQmTvBnZpLwRyHcDfGsAeUoViNbXqE").ShouldBe(SensitiveKind.None);
        Detect("kJxQmTvBnZpLwRyHcDfGsAeUoViNbXq1").ShouldBe(SensitiveKind.Token);
    }

    // --- priority and limits ---

    [Fact]
    public void When_several_kinds_are_present_the_most_serious_is_reported()
    {
        const string both = "-----BEGIN PRIVATE KEY----- 4111 1111 1111 1111 DE89 3704 0044 0532 0130 00";

        Detect(both).ShouldBe(SensitiveKind.PrivateKey);
        Detect(Jwt + " 4111 1111 1111 1111").ShouldBe(SensitiveKind.Jwt);
        Detect("4111 1111 1111 1111 DE89 3704 0044 0532 0130 00").ShouldBe(SensitiveKind.CardNumber);
        Detect("DE89 3704 0044 0532 0130 00 k3J9xQ2mT7vB5nZ8pL4wR6yH1cD0fGsA").ShouldBe(SensitiveKind.Iban);
    }

    [Fact]
    public void Only_the_first_million_characters_are_scanned()
    {
        var padding = new string(' ', SensitiveDetector.MaxScannedCharacters);

        Detect("4111 1111 1111 1111" + padding).ShouldBe(SensitiveKind.CardNumber);
        Detect(padding + "4111 1111 1111 1111").ShouldBe(SensitiveKind.None);
    }

    [Fact]
    public void A_huge_text_of_digits_and_spaces_is_scanned_in_linear_time()
    {
        var hostile = string.Concat(Enumerable.Repeat("1 2 3 4 5 6 7 8 9 0 ", 50_000));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Detect(hostile).ShouldBe(SensitiveKind.None);
        watch.Stop();

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }
}
