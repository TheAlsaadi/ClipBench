using System.Text;
using ClipBench.Core.Results;

namespace ClipBench.Core.Pasting;

/// <summary>The text placed between clips when they are joined.</summary>
public sealed record Separator
{
    /// <summary>The longest custom separator, in characters after escape sequences are resolved.</summary>
    public const int MaxCustomLength = 64;

    private Separator(string text) => Text = text;

    /// <summary>Nothing between the clips.</summary>
    public static Separator None { get; } = new("");

    /// <summary>A Windows line break (CR LF).</summary>
    public static Separator NewLine { get; } = new("\r\n");

    /// <summary>An empty line between the clips.</summary>
    public static Separator BlankLine { get; } = new("\r\n\r\n");

    /// <summary>One space.</summary>
    public static Separator Space { get; } = new(" ");

    /// <summary>One tab.</summary>
    public static Separator Tab { get; } = new("\t");

    /// <summary>A comma.</summary>
    public static Separator Comma { get; } = new(",");

    /// <summary>A comma and a space.</summary>
    public static Separator CommaSpace { get; } = new(", ");

    /// <summary>A semicolon.</summary>
    public static Separator Semicolon { get; } = new(";");

    /// <summary>The separator text.</summary>
    public string Text { get; }

    /// <summary>
    /// Reads a separator the user typed. The escape sequences \n (line feed), \r (carriage return),
    /// \t (tab) and \\ (backslash) are resolved, so the Windows line break is typed as \r\n.
    /// </summary>
    /// <param name="typed">The text as typed.</param>
    /// <returns>The separator, or a failure for an unknown escape sequence or a separator that is too long.</returns>
    public static Result<Separator> Parse(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);

        var text = new StringBuilder();
        for (var index = 0; index < typed.Length; index++)
        {
            if (typed[index] != '\\')
            {
                text.Append(typed[index]);
            }
            else if (index + 1 == typed.Length)
            {
                return Invalid("A separator cannot end with a lone backslash. Type \\\\ for one backslash.");
            }
            else
            {
                index++;
                switch (typed[index])
                {
                    case 'n':
                        text.Append('\n');
                        break;
                    case 'r':
                        text.Append('\r');
                        break;
                    case 't':
                        text.Append('\t');
                        break;
                    case '\\':
                        text.Append('\\');
                        break;
                    default:
                        return Invalid("Unknown escape sequence. Use \\n, \\r, \\t or \\\\.");
                }
            }

            // Stop reading at once: the typed text can be arbitrarily long.
            if (text.Length > MaxCustomLength)
            {
                return Invalid($"A separator can have at most {MaxCustomLength} characters.");
            }
        }

        return Result.Success(new Separator(text.ToString()));
    }

    private static Result<Separator> Invalid(string message) =>
        Result.Failure<Separator>(new ResultError(ErrorCode.InvalidSeparator, message));
}
