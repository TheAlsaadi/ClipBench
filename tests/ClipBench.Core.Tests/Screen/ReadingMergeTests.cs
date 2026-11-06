using ClipBench.Core.Screen;

namespace ClipBench.Core.Tests.Screen;

public class ReadingMergeTests
{
    private static string Word(params int[] codePoints) => string.Concat(codePoints.Select(cp => (char)cp));

    private static string Mohammed => Word(0x645, 0x62D, 0x645, 0x62F);

    private static string Alsaadi => Word(0x627, 0x644, 0x635, 0x627, 0x639, 0x62F, 0x64A);

    // A word at a place on a line of the picture (x from the left, y the line's top), 10 high.
    private static RecognizedWord W(string text, double x, double width, double y = 5) => new(text, x, y, width, 10);

    private static RecognizedLine Line(params RecognizedWord[] words) =>
        new(string.Join(' ', words.Select(word => word.Text)), words);

    private static RecognizedLine[] Reading(params RecognizedLine[] lines) => lines;

    // --- text of a reading ---

    [Fact]
    public void A_line_without_right_to_left_letters_keeps_the_text_windows_gave_it_even_if_the_words_differ()
    {
        var line = new RecognizedLine("hello  world", [W("hello", 0, 40), W("world", 60, 40)]);

        ReadingMerge.ToText(Reading(line)).ShouldBe("hello  world");
    }

    [Fact]
    public void A_right_to_left_line_is_put_in_reading_order_by_the_places_of_its_words()
    {
        // Listed out of order on purpose: the places decide.
        var line = new RecognizedLine(Mohammed + " " + Alsaadi, [W(Mohammed, 100, 40), W(Alsaadi, 10, 70)]);

        ReadingMerge.ToText(Reading(line)).ShouldBe(Mohammed + " " + Alsaadi);
    }

    [Fact]
    public void Lines_are_joined_with_a_line_break_and_no_lines_give_empty_text()
    {
        ReadingMerge.ToText(Reading(Line(W("one", 0, 30)), Line(W("two", 0, 30, 30)))).ShouldBe("one\r\ntwo");
        ReadingMerge.ToText(Reading()).ShouldBeEmpty();
    }

    // --- indentation, from where each line starts ---

    // A word of a monospaced font, 10 pixels a letter.
    private static RecognizedWord M(string text, double x, double y) => new(text, x, y, text.Length * 10, 10);

    [Fact]
    public void A_line_that_starts_further_right_than_the_others_is_indented_by_the_letters_that_fit_in_the_gap()
    {
        var lines = Reading(Line(M("def", 0, 0), M("f():", 40, 0)), Line(M("return", 40, 20), M("1", 110, 20)), Line(M("pass", 0, 40)));

        ReadingMerge.ToText(lines).ShouldBe("def f():\r\n    return 1\r\npass");
    }

    [Fact]
    public void Each_level_of_nesting_adds_its_own_indent()
    {
        var lines = Reading(Line(M("if", 0, 0), M("x:", 30, 0)), Line(M("while", 20, 20), M("y:", 70, 20)), Line(M("stop", 40, 40)));

        ReadingMerge.ToText(lines).ShouldBe("if x:\r\n  while y:\r\n    stop");
    }

    [Fact]
    public void A_stray_mark_left_of_the_text_does_not_indent_every_other_line()
    {
        // A border or a bullet read as a lone mark starts one line further left than the three lines that share a left edge.
        var lines = Reading(Line(M("|", 0, 0), M("alpha", 25, 0)), Line(M("beta", 25, 20)), Line(M("gamma", 25, 40)), Line(M("delta", 25, 60)));

        ReadingMerge.ToText(lines).ShouldBe("| alpha\r\nbeta\r\ngamma\r\ndelta");
    }

    [Fact]
    public void A_ragged_left_edge_of_less_than_half_a_letter_is_not_an_indent()
    {
        var lines = Reading(Line(M("first", 0, 0)), Line(M("second", 4, 20)), Line(M("third", 0, 40)));

        ReadingMerge.ToText(lines).ShouldBe("first\r\nsecond\r\nthird");
    }

    [Fact]
    public void Lines_with_no_places_for_their_words_are_not_indented_and_a_right_to_left_line_is_left_alone()
    {
        var plain = new RecognizedLine("no places", []);
        var arabic = new RecognizedLine(Mohammed, [M(Mohammed, 200, 20)]);

        ReadingMerge.ToText(Reading(Line(M("start", 0, 0)), plain, arabic)).ShouldBe("start\r\nno places\r\n" + Mohammed);
    }

    [Fact]
    public void An_indent_is_capped_so_a_wrong_place_cannot_make_a_huge_line()
    {
        var lines = Reading(Line(M("left", 0, 0)), Line(M("right", 5_000, 20)), Line(M("left", 0, 40)));

        var text = ReadingMerge.ToText(lines);

        text.Split("\r\n")[1].ShouldBe(new string(' ', 40) + "right");
    }

    // --- choosing among readings in different languages ---

    [Fact]
    public void Without_any_right_to_left_letters_the_reading_with_the_most_letters_wins_and_a_tie_goes_to_the_earlier()
    {
        var english = Reading(Line(W("2026", 0, 40)));
        var other = Reading(Line(W("hello", 0, 50)));

        ReadingMerge.Choose([english, other]).ShouldBeSameAs(other);
        ReadingMerge.Choose([Reading(Line(W("abc", 0, 30))), Reading(Line(W("xyz", 0, 30)))])[0].Text.ShouldBe("abc");
        ReadingMerge.Choose([]).ShouldBeEmpty();
    }

    [Fact]
    public void An_arabic_name_in_an_english_window_takes_its_letters_from_the_arabic_engine_and_everything_else_from_the_english_one()
    {
        // The reading of the user's File Explorer breadcrumb: three chevrons, an Arabic name, a folder.
        // English engine: the chevrons right, the Arabic as noise. Arabic engine: the Arabic right, the chevrons as "(".
        var english = Reading(Line(
            W(">", 59, 16),
            W("EIOJI", 134, 151),
            W(">", 446, 16),
            W("Minecraft-mod-porting", 518, 428),
            W(">", 1004, 16)));
        var arabic = Reading(Line(
            W("(", 59, 17),
            W(Alsaadi, 134, 152),
            W(Mohammed, 298, 92),
            W("(", 446, 17),
            W("Minecraft-mod-porting>", 518, 502)));

        var chosen = ReadingMerge.Choose([english, arabic]);

        ReadingMerge.ToText(chosen).ShouldBe("> " + Mohammed + " " + Alsaadi + " > Minecraft-mod-porting >");
    }

    [Fact]
    public void The_order_of_the_engines_does_not_change_the_merge()
    {
        var english = Reading(Line(W(">", 0, 10), W("EIOJI", 30, 100), W("folder", 150, 60)));
        var arabic = Reading(Line(W("(", 0, 10), W(Alsaadi, 30, 100), W("folder", 150, 60)));

        ReadingMerge.ToText(ReadingMerge.Choose([arabic, english])).ShouldBe(ReadingMerge.ToText(ReadingMerge.Choose([english, arabic])));
    }

    [Fact]
    public void The_arabic_engine_wins_even_when_the_noise_of_the_english_engine_has_more_letters()
    {
        var english = Reading(Line(W("EIOJIKLMN", 0, 100)));
        var arabic = Reading(Line(W(Mohammed, 0, 100)));

        ReadingMerge.ToText(ReadingMerge.Choose([english, arabic])).ShouldBe(Mohammed);
    }

    [Fact]
    public void Words_only_the_arabic_engine_found_are_kept_where_the_other_engine_found_nothing()
    {
        var english = Reading(Line(W(">", 0, 10)));
        var arabic = Reading(Line(W("(", 0, 10), W(Mohammed, 30, 60), W("x", 120, 10)));

        var text = ReadingMerge.ToText(ReadingMerge.Choose([english, arabic]));

        text.ShouldContain(Mohammed);
        text.ShouldContain("x");
    }

    [Fact]
    public void With_only_right_to_left_readings_the_best_one_is_used_as_it_is()
    {
        var one = Reading(Line(W("(", 0, 10), W(Alsaadi, 30, 60)));
        var two = Reading(Line(W(Mohammed, 30, 60)));

        ReadingMerge.Choose([one, two]).ShouldBeSameAs(one);
    }

    [Fact]
    public void One_stray_right_to_left_letter_is_noise_and_does_not_start_a_merge()
    {
        var english = Reading(Line(W("hello", 0, 50), W("world", 70, 50)));
        var noisy = Reading(Line(W("hello", 0, 50), W(Word(0x628), 70, 10)));

        ReadingMerge.Choose([english, noisy]).ShouldBeSameAs(english);
    }

    [Fact]
    public void A_word_below_an_arabic_word_at_the_same_place_is_on_another_line_and_is_kept()
    {
        var english = Reading(Line(W("top", 0, 40, 5)), Line(W("bottom", 0, 60, 40)));
        var arabic = Reading(Line(W(Mohammed, 0, 40, 5)));

        var text = ReadingMerge.ToText(ReadingMerge.Choose([english, arabic]));

        text.ShouldBe(Mohammed + "\r\nbottom");
    }

    [Fact]
    public void Symbols_alone_from_the_arabic_engine_are_not_added_to_the_other_engines_reading()
    {
        // The Arabic engine mirrors symbols ("(" for a chevron), so its symbols are only used when nothing else read the place.
        var english = Reading(Line(W("alpha", 0, 50)));
        var arabic = Reading(Line(W(Mohammed, 100, 60), W("(", 200, 10)));

        ReadingMerge.ToText(ReadingMerge.Choose([english, arabic])).ShouldBe("alpha " + Mohammed);
    }

    [Fact]
    public void The_merged_words_are_regrouped_into_lines_by_height_and_ordered_by_place()
    {
        var english = Reading(Line(W("alpha", 0, 50, 5)), Line(W("beta", 0, 40, 40)));
        var arabic = Reading(Line(W(Mohammed, 100, 60, 6)), Line(W("(", 100, 10, 41)));

        var text = ReadingMerge.ToText(ReadingMerge.Choose([english, arabic]));

        text.ShouldBe("alpha " + Mohammed + "\r\nbeta");
    }

    // --- a Latin-script reading that is preferred (the Tesseract engine) ---

    private static string Word2(params int[] codePoints) => string.Concat(codePoints.Select(cp => (char)cp));

    [Fact]
    public void A_preferred_latin_reading_wins_even_when_another_reading_has_more_letters()
    {
        var windows = Reading(Line(W("hello", 0, 50), W("world", 60, 50)));
        var latin = Reading(Line(W("x", 0, 10), W(">", 20, 10), W("0", 40, 10)));

        ReadingMerge.Choose([windows, latin], latinIndex: 1).ShouldBeSameAs(latin);
    }

    [Fact]
    public void A_preferred_reading_with_nothing_in_it_gives_way_to_the_best_of_the_others()
    {
        var windows = Reading(Line(W("hello", 0, 50)));

        ReadingMerge.Choose([windows, Reading()], latinIndex: 1).ShouldBeSameAs(windows);
        ReadingMerge.Choose([windows, Reading(Line(W(" ", 0, 10)))], latinIndex: 1).ShouldBeSameAs(windows);
    }

    [Fact]
    public void A_preferred_reading_of_symbols_only_is_still_the_reading_because_code_can_be_all_symbols()
    {
        var windows = Reading(Line(W("hello", 0, 50)));
        var symbols = Reading(Line(W("=>", 0, 20)));

        ReadingMerge.Choose([windows, symbols], latinIndex: 1).ShouldBeSameAs(symbols);
    }

    [Fact]
    public void A_reading_with_two_more_letters_the_english_model_cannot_read_beats_the_preferred_one()
    {
        // A Cyrillic or accented text read by its own Windows engine, while the English model guessed ASCII.
        var french = Reading(Line(W(Word2(0x00E9, 0x00E8, 0x00E0), 0, 40)));
        var guess = Reading(Line(W("eea", 0, 40)));
        var oneAccent = Reading(Line(W("caf" + (char)0x00E9, 0, 40)));
        var plain = Reading(Line(W("cafe", 0, 40)));

        ReadingMerge.Choose([french, guess], latinIndex: 1).ShouldBeSameAs(french);
        ReadingMerge.Choose([oneAccent, plain], latinIndex: 1).ShouldBeSameAs(plain);
    }

    // A word with the confidence (0 to 100) the Latin engine gave it.
    private static RecognizedWord WC(string text, double x, double width, double confidence) => new(text, x, 5, width, 10, confidence);

    [Fact]
    public void Arabic_letters_the_arabic_engine_made_up_do_not_replace_a_word_the_latin_engine_read_with_confidence()
    {
        // The Arabic engine reads "x > 0" at 14 pixels as a zero and a lam-alef, two right-to-left letters.
        var latin = Reading(Line(WC("x", 0, 10, 90), WC(">", 20, 10, 85), WC("0", 40, 10, 88)));
        var arabic = Reading(Line(W("O", 0, 10), W(Word2(0x0644, 0x0627), 20, 30)));

        ReadingMerge.ToText(ReadingMerge.Choose([arabic, latin], latinIndex: 1)).ShouldBe("x > 0");
    }

    [Fact]
    public void A_word_the_latin_engine_is_not_sure_of_gives_way_to_arabic_letters_in_the_same_place()
    {
        // The owner's breadcrumb: the English model made "ichalltax0" of an Arabic name, with confidence 0.
        var latin = Reading(Line(WC(">", 0, 16, 84), WC("ichalltax0", 30, 150, 0), WC("Minecraft-mod-porting", 250, 400, 88)));
        var arabic = Reading(Line(W("(", 0, 17), W(Alsaadi, 30, 70), W(Mohammed, 110, 70), W("Minecraft-mod-porting>", 250, 420)));

        ReadingMerge.ToText(ReadingMerge.Choose([arabic, latin], latinIndex: 1)).ShouldBe("> " + Mohammed + " " + Alsaadi + " Minecraft-mod-porting");
    }

    [Fact]
    public void A_word_has_no_confidence_unless_its_engine_gives_one()
    {
        new RecognizedWord("a", 0, 0, 1, 1).Confidence.ShouldBeNull();
        new RecognizedWord("a", 0, 0, 1, 1, 42).Confidence.ShouldBe(42);
    }

    [Fact]
    public void A_symbol_the_latin_engine_is_unsure_of_gives_way_to_the_symbol_another_engine_read_in_the_same_place()
    {
        // The English model reads a chevron icon as ">>" with confidence 42; the Windows engine reads ">".
        var latin = Reading(Line(WC("path", 0, 40, 90), WC(">>", 60, 16, 42), WC("name", 100, 40, 88)));
        var windows = Reading(Line(W("path", 0, 40), W(">", 60, 16), W("name", 100, 40)));

        ReadingMerge.ToText(ReadingMerge.Choose([latin, windows], latinIndex: 0)).ShouldBe("path > name");
    }

    [Fact]
    public void A_symbol_read_with_confidence_a_word_with_letters_or_a_place_nobody_else_read_is_left_alone()
    {
        var sure = Reading(Line(WC("=>", 0, 20, 80), WC("value", 40, 50, 90)));
        var unsureLetters = Reading(Line(WC("v4lue", 40, 50, 42)));
        var unsureAlone = Reading(Line(WC(">>", 0, 20, 42)));
        var windows = Reading(Line(W("=", 0, 10), W("value", 40, 50)));
        var elsewhere = Reading(Line(W(">", 300, 10)));
        var wordsOnly = Reading(Line(W("x", 0, 20)));

        ReadingMerge.ToText(ReadingMerge.Choose([sure, windows], latinIndex: 0)).ShouldBe("=> value");
        ReadingMerge.ToText(ReadingMerge.Choose([unsureLetters, windows], latinIndex: 0)).ShouldBe("v4lue");
        ReadingMerge.ToText(ReadingMerge.Choose([unsureAlone, elsewhere], latinIndex: 0)).ShouldBe(">>");
        ReadingMerge.ToText(ReadingMerge.Choose([unsureAlone, wordsOnly], latinIndex: 0)).ShouldBe(">>");
    }

    [Fact]
    public void The_slashed_zero_the_arabic_engine_reads_is_not_taken_for_an_alphabet_the_english_model_cannot_read()
    {
        // The Arabic engine reads a slashed zero as the Danish letter o-slash (U+00F8) and has no accent in its reading of code.
        var slash = ((char)0x00F8).ToString();
        var windows = Reading(Line(W("C:/Users/m" + slash + "hm" + slash + "d/C1ipBench" + slash + slash, 0, 200)));
        var latin = Reading(Line(W("C:/Users/mohmed/ClipBench00", 0, 200)));

        ReadingMerge.Choose([windows, latin], latinIndex: 1).ShouldBeSameAs(latin);
    }

    [Fact]
    public void Other_alphabets_and_letters_with_an_accent_are_what_the_english_model_cannot_read()
    {
        var cyrillic = Reading(Line(W(Word2(0x043F, 0x0440, 0x0438, 0x0432, 0x0435, 0x0442), 0, 60)));
        var greek = Reading(Line(W(Word2(0x03B1, 0x03B2, 0x03B3), 0, 60)));
        var guess = Reading(Line(W("npuBeT", 0, 60)));
        var accents = Reading(Line(W("caf" + (char)0x00E9 + " d" + (char)0x00E9 + "j" + (char)0x00E0, 0, 60)));
        var plain = Reading(Line(W("cafe deja", 0, 60)));

        ReadingMerge.Choose([cyrillic, guess], latinIndex: 1).ShouldBeSameAs(cyrillic);
        ReadingMerge.Choose([greek, guess], latinIndex: 1).ShouldBeSameAs(greek);
        ReadingMerge.Choose([accents, plain], latinIndex: 1).ShouldBeSameAs(accents);
    }

    [Fact]
    public void Arabic_words_come_from_the_arabic_engine_and_all_the_rest_from_the_preferred_reading()
    {
        var english = Reading(Line(W("path", 0, 40), W("noise", 100, 60), W(">", 200, 10)));
        var latin = Reading(Line(W("path", 0, 40), W("ichalltax0", 100, 60), W(">", 200, 10), W("C:/Users", 300, 80)));
        var arabic = Reading(Line(W("(", 0, 10), W(Mohammed, 100, 60), W("(", 200, 10), W("Users:C", 300, 80)));

        var text = ReadingMerge.ToText(ReadingMerge.Choose([english, arabic, latin], latinIndex: 2));

        text.ShouldBe("path " + Mohammed + " > C:/Users");
    }

    [Fact]
    public void A_preferred_reading_that_has_right_to_left_letters_is_not_trusted_as_the_latin_one()
    {
        var windows = Reading(Line(W("hello", 0, 50)));
        var odd = Reading(Line(W(Mohammed, 0, 50)));

        // A reading with right-to-left letters is no Latin reading: the choice is the one made with no preferred reading.
        ReadingMerge.ToText(ReadingMerge.Choose([windows, odd], latinIndex: 1))
            .ShouldBe(ReadingMerge.ToText(ReadingMerge.Choose([windows, odd], latinIndex: -1)));
    }

    [Fact]
    public void Choose_without_a_preferred_reading_behaves_as_before()
    {
        var english = Reading(Line(W("2026", 0, 40)));
        var other = Reading(Line(W("hello", 0, 50)));

        ReadingMerge.Choose([english, other], latinIndex: -1).ShouldBeSameAs(other);
    }

    [Fact]
    public void Missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => ReadingMerge.ToText(null!));
        Should.Throw<ArgumentNullException>(() => ReadingMerge.Choose(null!));
        Should.Throw<ArgumentNullException>(() => ReadingMerge.Choose(null!, 0));
    }
}
