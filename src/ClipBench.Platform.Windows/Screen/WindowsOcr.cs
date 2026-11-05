using System.Collections.Concurrent;
using ClipBench.Core.Screen;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.System.UserProfile;

namespace ClipBench.Platform.Windows.Screen;

/// <summary>Builds the text recognizer the app uses: Windows' recognition in every language of the user's profile, on enlarged small text.</summary>
public static class WindowsOcr
{
    /// <summary>The most languages tried for one picture. A reading takes about a tenth of a second per language.</summary>
    private const int MaxLanguages = 4;

    private const string LanguageMessage =
        "Windows has no text-recognition language for your languages. Open Settings → Time & language → Language & region, "
        + "choose a language, then Language options, and add its Text recognition feature. Then try again.";

    /// <summary>Creates the recognizer for the languages in the user's Windows profile, in the order the user ranked them.</summary>
    /// <returns>The recognizer.</returns>
    public static ITextRecognizer CreateRecognizer() => CreateRecognizer(() => [.. GlobalizationPreferences.Languages]);

    /// <summary>Creates a recognizer for the given language tags (for tests).</summary>
    /// <param name="languageTags">The tags, asked for every time because the user can add a language while ClipBench runs.</param>
    /// <returns>The recognizer.</returns>
    internal static ITextRecognizer CreateRecognizer(Func<IReadOnlyList<string>> languageTags)
    {
        ArgumentNullException.ThrowIfNull(languageTags);

        // An engine is made once per language. A language without the feature is looked for again next time, so adding it takes effect at once.
        var engines = new ConcurrentDictionary<string, WindowsOcrEngine>(StringComparer.OrdinalIgnoreCase);
        return new AdaptiveRecognizer(() => EnginesFor(languageTags(), engines), LanguageMessage);
    }

    private static List<ITextRecognizer> EnginesFor(IReadOnlyList<string> tags, ConcurrentDictionary<string, WindowsOcrEngine> cache)
    {
        var found = new List<ITextRecognizer>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            if (found.Count >= MaxLanguages)
            {
                break;
            }

            if (!seen.Add(tag))
            {
                continue;
            }

            if (cache.TryGetValue(tag, out var known))
            {
                found.Add(known);
                continue;
            }

            var engine = TryCreate(tag);
            if (engine is not null)
            {
                found.Add(cache.GetOrAdd(tag, _ => new WindowsOcrEngine(engine)));
            }
        }

        return found;
    }

    private static OcrEngine? TryCreate(string tag)
    {
        try
        {
            return OcrEngine.IsLanguageSupported(new Language(tag)) ? OcrEngine.TryCreateFromLanguage(new Language(tag)) : null;
        }
        catch (ArgumentException)
        {
            // Not a language tag.
            return null;
        }
    }
}
