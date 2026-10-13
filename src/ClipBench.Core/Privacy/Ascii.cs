namespace ClipBench.Core.Privacy;

/// <summary>Character tests that look at ASCII only, which is all the secret formats use.</summary>
internal static class Ascii
{
    public static bool IsLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    public static bool IsDigit(char c) => c is >= '0' and <= '9';

    public static bool IsLetterOrDigit(char c) => IsLetter(c) || IsDigit(c);
}
