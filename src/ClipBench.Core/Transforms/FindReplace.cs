using System.Text.RegularExpressions;
using ClipBench.Core.Results;

namespace ClipBench.Core.Transforms;

/// <summary>What to find and what to put in its place. Never holds clip content, but what the user typed can be private, so printing hides it.</summary>
public sealed class FindReplaceOptions
{
    /// <summary>The text to find, or the pattern when <see cref="UseRegex"/> is set.</summary>
    public string Find
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = string.Empty;

    /// <summary>The text that replaces each match. With <see cref="UseRegex"/> it may use $1, ${name} and $&amp; as .NET does.</summary>
    public string Replace
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = string.Empty;

    /// <summary>True to read <see cref="Find"/> as a regular expression.</summary>
    public bool UseRegex { get; init; }

    /// <summary>True to ignore the difference between capital and small letters (by the invariant culture).</summary>
    public bool IgnoreCase { get; init; }

    /// <summary>Printing never shows what the user typed.</summary>
    /// <returns>The type name.</returns>
    public override string ToString() => nameof(FindReplaceOptions);
}

/// <summary>Builds the find-and-replace transform. Patterns are bounded in length and in time.</summary>
public static class FindReplace
{
    /// <summary>The longest text to find or pattern, in characters.</summary>
    public const int MaxPatternLength = 1000;

    /// <summary>The longest replacement, in characters.</summary>
    public const int MaxReplacementLength = 10_000;

    private const string WindowsBreak = "\r\n";
    private const string UnixBreak = "\n";

    /// <summary>How long one pattern may take on one text before it is given up.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Checks the options and builds a transform that does the replacing.</summary>
    /// <param name="options">What to find and replace.</param>
    /// <param name="timeout">The time one run may take. Defaults to <see cref="DefaultTimeout"/>.</param>
    /// <param name="maxCharacters">The most characters the result may have.</param>
    /// <returns>The transform, or why the options cannot be used.</returns>
    public static Result<TextTransform> Create(FindReplaceOptions options, TimeSpan? timeout = null, int maxCharacters = Pasting.PasteComposer.DefaultMaxOutputCharacters)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, 1);
        var limit = timeout ?? DefaultTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, TimeSpan.Zero);

        if (options.Find.Length == 0)
        {
            return Refused("Enter the text to find.");
        }

        if (options.Find.Length > MaxPatternLength)
        {
            return Refused($"The text to find is too long (at most {MaxPatternLength:N0} characters).");
        }

        if (options.Replace.Length > MaxReplacementLength)
        {
            return Refused($"The replacement is too long (at most {MaxReplacementLength:N0} characters).");
        }

        if (!options.UseRegex)
        {
            return Result.Success(Wrap(text => ReplacePlain(text, options, maxCharacters)));
        }

        var regex = Compile(options, limit, out var failure);
        return regex is null
            ? Result.Failure<TextTransform>(failure)
            : Result.Success(Wrap(text => ReplacePattern(text, regex, options.Replace, maxCharacters)));
    }

    private static TextTransform Wrap(Func<string, Result<string>> apply) =>
        new("text.find-replace", "Find and replace", "Replace text, or use a pattern", apply);

    private static Result<string> ReplacePlain(string text, FindReplaceOptions options, int maxCharacters)
    {
        var comparison = options.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        long count = 0;
        for (var at = text.IndexOf(options.Find, comparison); at >= 0; at = text.IndexOf(options.Find, at + options.Find.Length, comparison))
        {
            count++;
        }

        if (count == 0)
        {
            return Result.Success(text);
        }

        return text.Length + (count * (options.Replace.Length - options.Find.Length)) > maxCharacters
            ? TooLarge()
            : Result.Success(text.Replace(options.Find, options.Replace, comparison));
    }

    // "$" in a pattern matches before a line feed only, so a text with Windows line breaks is matched with plain line
    // feeds and given its line breaks back. Text with any other mix of line breaks is matched as it is.
    private static Result<string> ReplacePattern(string text, Regex regex, string replacement, int maxCharacters)
    {
        var windowsLines = HasOnlyWindowsLineBreaks(text);
        var input = windowsLines ? text.Replace(WindowsBreak, UnixBreak, StringComparison.Ordinal) : text;

        long growth = 0;
        var tooLarge = false;
        string replaced;
        try
        {
            replaced = regex.Replace(input, match =>
            {
                if (tooLarge)
                {
                    return string.Empty;
                }

                var piece = match.Result(replacement);
                growth += piece.Length - match.Length;
                if (input.Length + growth > maxCharacters)
                {
                    tooLarge = true;
                    return string.Empty;
                }

                return piece;
            });
        }
        catch (RegexMatchTimeoutException)
        {
            return Result.Failure<string>(new ResultError(ErrorCode.PatternTimedOut, "The pattern took too long on this text, so nothing was changed. Try a simpler pattern."));
        }

        if (tooLarge)
        {
            return TooLarge();
        }

        return Result.Success(windowsLines ? replaced.Replace(UnixBreak, WindowsBreak, StringComparison.Ordinal) : replaced);
    }

    private static bool HasOnlyWindowsLineBreaks(string text)
    {
        var windows = 0;
        for (var at = text.IndexOf(WindowsBreak, StringComparison.Ordinal); at >= 0; at = text.IndexOf(WindowsBreak, at + WindowsBreak.Length, StringComparison.Ordinal))
        {
            windows++;
        }

        return windows > 0
            && text.AsSpan().Count('\r') == windows
            && text.AsSpan().Count('\n') == windows;
    }

    // The engine that cannot backtrack is used whenever the pattern allows it, so most patterns cannot be made to take
    // exponential time. Patterns that need backtracking (look-around, back references) run with the timeout alone.
    private static Regex? Compile(FindReplaceOptions options, TimeSpan timeout, out ResultError failure)
    {
        var flags = RegexOptions.CultureInvariant | RegexOptions.Multiline | (options.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        try
        {
            try
            {
                failure = default;
                return new Regex(options.Find, flags | RegexOptions.NonBacktracking, timeout);
            }
            catch (NotSupportedException)
            {
                failure = default;
                return new Regex(options.Find, flags, timeout);
            }
        }
        catch (RegexParseException exception)
        {
            failure = new ResultError(ErrorCode.PatternInvalid, $"The pattern is not valid (at character {exception.Offset + 1}).");
            return null;
        }
        catch (ArgumentException)
        {
            failure = new ResultError(ErrorCode.PatternInvalid, "The pattern is not valid.");
            return null;
        }
    }

    private static Result<TextTransform> Refused(string message) =>
        Result.Failure<TextTransform>(new ResultError(ErrorCode.PatternInvalid, message));

    private static Result<string> TooLarge() =>
        Result.Failure<string>(new ResultError(ErrorCode.OutputTooLarge, "The result would be too large to paste."));
}
