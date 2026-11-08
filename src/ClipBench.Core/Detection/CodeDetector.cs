using System.Buffers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClipBench.Core.Detection;

/// <summary>
/// A simple guess whether a text is source code or markup, and in which language. It adds up weighted signals (a
/// keyword with its punctuation, a header line, a typical call) and names the language with the highest score, if it
/// is high enough. It is a heuristic: it can be wrong in both directions. Only the start of a long text is read, and every
/// pattern runs on the non-backtracking engine with a time limit, so the work is bounded by the length read.
/// </summary>
public static class CodeDetector
{
    /// <summary>Only this many characters at the start of a text are read for signals.</summary>
    public const int MaxScanned = 4_000;

    /// <summary>A text longer than this is not read as JSON.</summary>
    public const int MaxJsonLength = 200_000;

    private const int JsonDepth = 64;
    private const int Threshold = 3;
    private const RegexOptions Options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.Multiline;
    private static readonly TimeSpan _timeout = TimeSpan.FromMilliseconds(250);
    private static readonly SearchValues<char> _codeCharacters = SearchValues.Create("{};=()<>#$:&|");

    private static readonly Lazy<Language[]> _languages = new(BuildLanguages);
    private static readonly Lazy<Regex> _tag = new(() => Create(@"<[A-Za-z][A-Za-z0-9-]*(\s[^<>]*)?/?>"));
    private static readonly Lazy<Regex> _htmlTag = new(() => Create(@"</?(div|span|p|a|body|head|html|ul|ol|li|table|tr|td|th|img|br|h[1-6]|script|style|button|input|form|section|nav|header|footer|label|select|option)\b"));
    private static readonly Lazy<Regex> _lineEnding = new(() => Create(@"[;{}]\s*$"));

    /// <summary>
    /// Builds the patterns now. They take a few milliseconds each to set up, so the app calls this on a background thread at start-up
    /// and the first popup that lists clips does not wait for them. Safe to call more than once and from any thread.
    /// </summary>
    public static void Warm()
    {
        _ = _languages.Value;
        _ = _tag.Value;
        _ = _htmlTag.Value;
        _ = _lineEnding.Value;
    }

    /// <summary>Guesses what language the text is written in.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The language (such as "C#", "Python", "SQL", "JSON", "HTML"), or null when the text does not look like code.</returns>
    public static string? Guess(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (LooksLikeJson(trimmed))
        {
            return "JSON";
        }

        var head = trimmed.Length > MaxScanned ? trimmed[..MaxScanned] : trimmed;
        if (Markup(head, trimmed) is { } markup)
        {
            return markup;
        }

        // Prose has hardly any of the characters code is made of.
        if (head.AsSpan().IndexOfAny(_codeCharacters) < 0)
        {
            return null;
        }

        Language? best = null;
        var bestScore = 0;
        foreach (var language in _languages.Value)
        {
            var score = language.Score(head);
            if (score > bestScore)
            {
                best = language;
                bestScore = score;
            }
        }

        if (best is null)
        {
            return null;
        }

        // Several lines that end like code lines tip a score that is a little low.
        if (bestScore < Threshold && !(bestScore >= 2 && CodeLikeLines(head)))
        {
            return null;
        }

        return best.Name;
    }

    private static bool LooksLikeJson(string text)
    {
        if (text.Length > MaxJsonLength || !((text[0] == '{' && text[^1] == '}') || (text[0] == '[' && text[^1] == ']')))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = JsonDepth });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Markup(string head, string whole)
    {
        if (head[0] != '<' || whole[^1] != '>')
        {
            return null;
        }

        if (head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            return "XML";
        }

        if (head.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || Matches(_htmlTag.Value, head))
        {
            return "HTML";
        }

        return Matches(_tag.Value, head) ? "XML" : null;
    }

    private static bool CodeLikeLines(string head)
    {
        var lines = head.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length >= 2 && lines.Count(line => Matches(_lineEnding.Value, line)) >= 2;
    }

    private static Regex Create(string pattern) => new(pattern, Options, _timeout);

    // A signal whose pattern runs out of time (a slow or busy machine) counts as absent: the guess may be weaker, but the
    // popup and the clip details that ask for it never see an exception.
    internal static bool Matches(Regex regex, string text)
    {
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static Language[] BuildLanguages()
    {
        // Each signal counts once, however often it appears.
        var javaScript = new (string Pattern, int Weight)[]
        {
            (@"\b(const|let)\s+\w+\s*=", 2),
            (@"=>", 1),
            (@"\bconsole\.\w+\(", 3),
            (@"\bfunction\s+\w+\s*\(", 2),
            (@"\brequire\(", 2),
            (@"\bimport\s+[\w{}*, ]+\s+from\s+['""]", 3),
            (@"\bdocument\.\w+", 2),
            (@"===", 1),
        };
        var typeScript = new (string Pattern, int Weight)[]
        {
            (@"\binterface\s+\w+\s*\{", 3),
            (@":\s*(string|number|boolean|void|any)\b", 2),
            (@"\btype\s+\w+\s*=", 2),
        };

        return
        [
            new Language("C#", Build(
                (@"^\s*using\s+[A-Z][\w.]*;", 3),
                (@"\bnamespace\s+[\w.]+\s*[;{]", 3),
                (@"\b(public|private|internal|protected)\s+(static\s+)?(class|void|string|int|bool|async|record|interface)\b", 2),
                (@"\bConsole\.Write(Line)?\(", 3),
                (@"=>", 1),
                (@"\bvar\s+\w+\s*=", 1))),
            new Language("TypeScript", Build([.. javaScript, .. typeScript]), required: typeScript.Length),
            new Language("JavaScript", Build(javaScript)),
            new Language("Python", Build(
                (@"^\s*def\s+\w+\(.*\)\s*(->.*)?:\s*$", 4),
                (@"^from\s+[\w.]+\s+import\s+", 3),
                (@"^import\s+\w+(\.\w+)*\s*$", 2),
                (@"^\s*class\s+\w+(\(.*\))?:\s*$", 3),
                (@"\bself\.\w+", 2),
                (@"\belif\b", 2),
                (@"\bprint\(", 1),
                (@"^\s*(if|for|while|else|try|except|with)\b.*:\s*$", 1))),
            new Language("Java", Build(
                (@"\bpublic\s+static\s+void\s+main", 4),
                (@"\bSystem\.out\.print", 4),
                (@"^\s*import\s+java\.", 3),
                (@"\b(public|private|protected)\s+class\s+\w+", 1),
                (@"@Override", 2),
                (@"\bString\[\]", 2))),
            new Language("C/C++", Build(
                (@"^\s*#include\s*[<""]", 4),
                (@"\bint\s+main\s*\(", 3),
                (@"\bstd::", 3),
                (@"\bprintf\(", 2),
                (@"\b(cout|cin)\b", 2),
                (@"\bnullptr\b", 2))),
            new Language("Go", Build(
                (@"^package\s+\w+", 3),
                (@"\bfunc\s+(\(\w+\s+\*?\w+\)\s*)?\w+\(", 3),
                (@"\bfmt\.\w+", 3),
                (@":=", 2),
                (@"^import\s+(\(|"")", 2))),
            new Language("Rust", Build(
                (@"\bfn\s+\w+\s*\(", 3),
                (@"\blet\s+mut\b", 3),
                (@"\bprintln!\(", 3),
                (@"\buse\s+std::", 3),
                (@"\bimpl\b", 1))),
            new Language("SQL", Build(
                (@"\bSELECT\b[^;]*\bFROM\b", 4),
                (@"\bINSERT\s+INTO\b", 4),
                (@"\bCREATE\s+(TABLE|INDEX|VIEW)\b", 4),
                (@"\bUPDATE\s+\w+\s+SET\b", 4),
                (@"\bDELETE\s+FROM\b", 4),
                (@"\bWHERE\b", 1),
                (@"\bORDER\s+BY\b", 1),
                (@"\bJOIN\b", 1))),
            new Language("Shell", Build(
                (@"^#!\s*/(usr/)?bin/(env\s+)?(ba|z|da)?sh", 4),
                (@"^\$\s+\w+", 2),
                (@"\bsudo\s+\w+", 3),
                (@"\|\s*(grep|awk|sed|sort|head|tail|xargs)\b", 3),
                (@"\b(apt|apt-get|yum|brew|npm|pip|dnf)\s+(install|update|upgrade|add)\b", 3),
                (@"^\s*(cd|ls|rm|cp|mv|cat|echo|export)\s+[-~/$""'\w.]", 1),
                (@"&&", 1),
                (@"\bset\s+-e\b", 2))),
            new Language("PowerShell", Build(
                (@"\b(Get|Set|New|Remove|Write|Where|Select|ForEach|Invoke|Import|Out|Start|Stop|Test)-[A-Z][A-Za-z]+\b", 3),
                (@"\$env:\w+", 3),
                (@"\$_\b", 2),
                (@"\bparam\s*\(", 2),
                (@"\s-(eq|ne|gt|lt|ge|le|like|match)\b", 1))),
            new Language("CSS", Build(
                (@"[.#]?[\w-]+\s*\{[^{}]*\b[a-z-]+\s*:\s*[^;{}]+;", 4))),
        ];
    }

    private static (Regex Regex, int Weight)[] Build(params (string Pattern, int Weight)[] signals) =>
        [.. signals.Select(signal => (Create(signal.Pattern), signal.Weight))];

    private sealed class Language(string name, (Regex Regex, int Weight)[] signals, int required = 0)
    {
        public string Name { get; } = name;

        // The last <required> signals must all be absent for the language not to apply: a language that extends another
        // (TypeScript over JavaScript) only counts when something of its own is present.
        public int Score(string text)
        {
            var total = 0;
            var own = 0;
            for (var i = 0; i < signals.Length; i++)
            {
                if (Matches(signals[i].Regex, text))
                {
                    total += signals[i].Weight;
                    if (i >= signals.Length - required)
                    {
                        own += signals[i].Weight;
                    }
                }
            }

            return required > 0 && own == 0 ? 0 : total;
        }
    }
}
