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
        return new AdaptiveRecognizer(() => [.. EnginesFor(languageTags(), engines)], LanguageMessage);
    }

    /// <summary>The languages that would be read for the given tags (for tests): the recognizer language of each engine, in order.</summary>
    /// <param name="languageTags">The tags.</param>
    /// <returns>The tags of the engines.</returns>
    internal static List<string> EngineTags(Func<IReadOnlyList<string>> languageTags)
    {
        ArgumentNullException.ThrowIfNull(languageTags);
        return [.. EnginesFor(languageTags(), new ConcurrentDictionary<string, WindowsOcrEngine>(StringComparer.OrdinalIgnoreCase)).Select(engine => engine.LanguageTag)];
    }

    private static List<WindowsOcrEngine> EnginesFor(IReadOnlyList<string> tags, ConcurrentDictionary<string, WindowsOcrEngine> cache)
    {
        var found = new List<WindowsOcrEngine>();
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            if (found.Count >= MaxLanguages)
            {
                break;
            }

            var engine = cache.TryGetValue(tag, out var known) ? known : TryCreate(tag);

            // Varieties of one language (en-US, en-GB, and en-AU which has no engine of its own) read the same text:
            // one pass each would spend the few allowed on English and never reach a second script.
            if (engine is null || !languages.Add(LanguageOf(engine.LanguageTag)))
            {
                continue;
            }

            found.Add(cache.GetOrAdd(tag, engine));
        }

        return found;
    }

    // "en-US" and "en-GB" are one language; "zh-Hans-CN" and "zh-Hant-TW" are two scripts and stay apart.
    private static string LanguageOf(string tag)
    {
        var parts = tag.Split('-');
        return parts.Length > 1 && parts[1].Length == 4 ? parts[0] + "-" + parts[1] : parts[0];
    }

    private static WindowsOcrEngine? TryCreate(string tag)
    {
        try
        {
            var language = new Language(tag);
            var engine = OcrEngine.IsLanguageSupported(language) ? OcrEngine.TryCreateFromLanguage(language) : null;
            return engine is null ? null : new WindowsOcrEngine(engine);
        }
        catch (ArgumentException)
        {
            // Not a language tag.
            return null;
        }
    }
}
