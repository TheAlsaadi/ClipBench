namespace ClipBench.Core.Privacy;

/// <summary>What kind of secret a text looks like, most serious first.</summary>
public enum SensitiveKind
{
    /// <summary>Nothing that looks like a secret.</summary>
    None = 0,

    /// <summary>A private key block (PEM).</summary>
    PrivateKey,

    /// <summary>A JSON web token.</summary>
    Jwt,

    /// <summary>A payment card number.</summary>
    CardNumber,

    /// <summary>An international bank account number.</summary>
    Iban,

    /// <summary>A long random-looking token, such as an API key.</summary>
    Token,
}
