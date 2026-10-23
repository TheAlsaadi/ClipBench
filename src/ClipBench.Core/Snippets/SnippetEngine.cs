using System.Globalization;
using System.Text;
using ClipBench.Core.Results;

namespace ClipBench.Core.Snippets;

/// <summary>What kind of problem a snippet has. The paste still goes ahead; the snippet editor shows these.</summary>
public enum SnippetWarningKind
{
    /// <summary>The text in braces is not a variable. It is left as it is.</summary>
    UnknownVariable,

    /// <summary>The format of a date or time is longer than the limit or is not a format. The variable is left as it is.</summary>
    InvalidFormat,

    /// <summary>The name of an input is missing, too long or has characters it may not have. The variable is left as it is.</summary>
    InvalidInputName,

    /// <summary>A brace is opened and never closed. It is left as it is.</summary>
    UnclosedBrace,
}

/// <summary>A problem found in a snippet, and where. It holds no text of the snippet.</summary>
/// <param name="Kind">What is wrong.</param>
/// <param name="Position">Where the problem starts in the snippet, counting characters from 0.</param>
/// <param name="Length">How many characters it covers.</param>
public readonly record struct SnippetWarning(SnippetWarningKind Kind, int Position, int Length);

/// <summary>The values a snippet is filled in with, apart from the date and the time.</summary>
/// <param name="ClipboardText">The text of the newest text clip, or null when there is none.</param>
/// <param name="Inputs">What the user typed for each <c>{input:Name}</c>, by name.</param>
public sealed record SnippetValues(string? ClipboardText, IReadOnlyDictionary<string, string>? Inputs)
{
    /// <summary>Printing never shows the clipboard text or what was typed.</summary>
    /// <returns>The type name.</returns>
    public override string ToString() => nameof(SnippetValues);
}

internal enum SnippetTokenKind
{
    Text,
    Date,
    Time,
    Clipboard,
    Input,
}

// For Text the text itself; for Date and Time the format; for Input the name.
internal readonly record struct SnippetToken(SnippetTokenKind Kind, string Text);

/// <summary>A snippet that has been read: what it asks for and what is wrong with it. Immutable, and it can be filled in many times.</summary>
public sealed class ParsedSnippet
{
    internal ParsedSnippet(IReadOnlyList<SnippetToken> tokens, IReadOnlyList<string> inputNames, IReadOnlyList<SnippetWarning> warnings)
    {
        Tokens = tokens;
        InputNames = inputNames;
        Warnings = warnings;
    }

    /// <summary>The names of the values the snippet asks for, each once, in the order they first appear.</summary>
    public IReadOnlyList<string> InputNames { get; }

    /// <summary>The problems found, in the order they appear.</summary>
    public IReadOnlyList<SnippetWarning> Warnings { get; }

    internal IReadOnlyList<SnippetToken> Tokens { get; }

    /// <summary>Printing never shows the text of the snippet.</summary>
    /// <returns>The type name.</returns>
    public override string ToString() => nameof(ParsedSnippet);
}

/// <summary>Reads snippets and fills them in. See ADR 0016 for the syntax.</summary>
public sealed class SnippetEngine
{
    /// <summary>The longest snippet, in characters.</summary>
    public const int MaxBodyLength = 10_000;

    /// <summary>The longest date or time format, in characters.</summary>
    public const int MaxFormatLength = 40;

    /// <summary>The longest name of an input, in characters.</summary>
    public const int MaxInputNameLength = 40;

    /// <summary>The longest value for one input, in characters.</summary>
    public const int MaxInputLength = 10_000;

    /// <summary>The most different inputs one snippet may ask for.</summary>
    public const int MaxInputs = 20;

    /// <summary>The longest result, in characters.</summary>
    public const int MaxOutputLength = 100_000;

    private const string DefaultDateFormat = "yyyy-MM-dd";
    private const string DefaultTimeFormat = "HH:mm";

    // Any date will do to find out whether a format can be used.
    private static readonly DateTimeOffset _sample = new(2000, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TimeProvider _timeProvider;

    /// <summary>Creates an engine.</summary>
    /// <param name="timeProvider">The clock for <c>{date}</c> and <c>{time}</c>, in its local time zone.</param>
    public SnippetEngine(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <summary>Reads a snippet.</summary>
    /// <param name="body">The text of the snippet.</param>
    /// <returns>The snippet, or why it cannot be used: it is too long, or asks for too many values. Other problems are warnings.</returns>
    public static Result<ParsedSnippet> Parse(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.Length > MaxBodyLength)
        {
            return Result.Failure<ParsedSnippet>(new ResultError(ErrorCode.InvalidSnippet, $"A snippet can have at most {MaxBodyLength:N0} characters."));
        }

        var tokens = new List<SnippetToken>();
        var warnings = new List<SnippetWarning>();
        var names = new List<string>();
        var text = new StringBuilder();

        var index = 0;
        while (index < body.Length)
        {
            var c = body[index];
            if (c == '{' && index + 1 < body.Length && body[index + 1] == '{')
            {
                _ = text.Append('{');
                index += 2;
            }
            else if (c == '}' && index + 1 < body.Length && body[index + 1] == '}')
            {
                _ = text.Append('}');
                index += 2;
            }
            else if (c == '{')
            {
                var close = body.IndexOf('}', index + 1);
                if (close < 0)
                {
                    _ = text.Append(body, index, body.Length - index);
                    warnings.Add(new SnippetWarning(SnippetWarningKind.UnclosedBrace, index, body.Length - index));
                    index = body.Length;
                    continue;
                }

                var length = close - index + 1;
                var read = ReadVariable(body.AsSpan(index + 1, length - 2));
                if (read.Token is { } token)
                {
                    FlushText(tokens, text);
                    tokens.Add(token);
                    if (token.Kind == SnippetTokenKind.Input && !names.Contains(token.Text, StringComparer.OrdinalIgnoreCase))
                    {
                        if (names.Count == MaxInputs)
                        {
                            return Result.Failure<ParsedSnippet>(new ResultError(ErrorCode.InvalidSnippet, $"A snippet can ask for at most {MaxInputs} different values."));
                        }

                        names.Add(token.Text);
                    }
                }
                else
                {
                    _ = text.Append(body, index, length);
                    warnings.Add(new SnippetWarning(read.Problem, index, length));
                }

                index = close + 1;
            }
            else
            {
                _ = text.Append(c);
                index++;
            }
        }

        FlushText(tokens, text);
        return Result.Success(new ParsedSnippet(tokens, names, warnings));
    }

    /// <summary>Fills a snippet in. The text is read once from left to right, and what is put in is never read again.</summary>
    /// <param name="snippet">The snippet.</param>
    /// <param name="values">The clipboard text and the typed values.</param>
    /// <returns>The text, or why it could not be made: a value is missing or too long, or the result is too long.</returns>
    public Result<string> Expand(ParsedSnippet snippet, SnippetValues values)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        ArgumentNullException.ThrowIfNull(values);

        var now = _timeProvider.GetLocalNow();
        var result = new StringBuilder();
        foreach (var token in snippet.Tokens)
        {
            string piece;
            switch (token.Kind)
            {
                case SnippetTokenKind.Date:
                case SnippetTokenKind.Time:
                    piece = now.ToString(token.Text, CultureInfo.InvariantCulture);
                    break;
                case SnippetTokenKind.Clipboard:
                    piece = values.ClipboardText ?? string.Empty;
                    break;
                case SnippetTokenKind.Input:
                    var typed = FindInput(values.Inputs, token.Text);
                    if (typed is null)
                    {
                        return Result.Failure<string>(new ResultError(ErrorCode.SnippetInputMissing, "The snippet needs a value that was not given."));
                    }

                    if (typed.Length > MaxInputLength)
                    {
                        return Result.Failure<string>(new ResultError(ErrorCode.SnippetInputTooLong, $"A value can have at most {MaxInputLength:N0} characters."));
                    }

                    piece = typed;
                    break;
                default:
                    piece = token.Text;
                    break;
            }

            if (result.Length + piece.Length > MaxOutputLength)
            {
                return Result.Failure<string>(new ResultError(ErrorCode.OutputTooLarge, "The snippet would be too long to paste."));
            }

            _ = result.Append(piece);
        }

        return Result.Success(result.ToString());
    }

    private static void FlushText(List<SnippetToken> tokens, StringBuilder text)
    {
        if (text.Length > 0)
        {
            tokens.Add(new SnippetToken(SnippetTokenKind.Text, text.ToString()));
            _ = text.Clear();
        }
    }

    // Reads what is between the braces: a variable, or the kind of problem it has.
    private static (SnippetToken? Token, SnippetWarningKind Problem) ReadVariable(ReadOnlySpan<char> inner)
    {
        var colon = inner.IndexOf(':');
        var name = colon < 0 ? inner : inner[..colon];
        string? argument = colon < 0 ? null : inner[(colon + 1)..].ToString();

        if (name.Equals("date", StringComparison.OrdinalIgnoreCase))
        {
            return Formatted(SnippetTokenKind.Date, argument, DefaultDateFormat);
        }

        if (name.Equals("time", StringComparison.OrdinalIgnoreCase))
        {
            return Formatted(SnippetTokenKind.Time, argument, DefaultTimeFormat);
        }

        if (name.Equals("clipboard", StringComparison.OrdinalIgnoreCase) && argument is null)
        {
            return (new SnippetToken(SnippetTokenKind.Clipboard, string.Empty), default);
        }

        if (name.Equals("input", StringComparison.OrdinalIgnoreCase))
        {
            var inputName = argument?.Trim();
            return inputName is not null && IsInputName(inputName)
                ? (new SnippetToken(SnippetTokenKind.Input, inputName), default)
                : (null, SnippetWarningKind.InvalidInputName);
        }

        return (null, SnippetWarningKind.UnknownVariable);
    }

    private static (SnippetToken? Token, SnippetWarningKind Problem) Formatted(SnippetTokenKind kind, string? argument, string defaultFormat)
    {
        var format = argument ?? defaultFormat;
        return IsUsableFormat(format)
            ? (new SnippetToken(kind, format), default)
            : (null, SnippetWarningKind.InvalidFormat);
    }

    private static bool IsUsableFormat(string format)
    {
        if (format.Length is 0 or > MaxFormatLength)
        {
            return false;
        }

        try
        {
            _ = _sample.ToString(format, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsInputName(string name)
    {
        if (name.Length is 0 or > MaxInputNameLength)
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c is not (' ' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static string? FindInput(IReadOnlyDictionary<string, string>? inputs, string name)
    {
        if (inputs is null)
        {
            return null;
        }

        foreach (var (key, value) in inputs)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }
}
