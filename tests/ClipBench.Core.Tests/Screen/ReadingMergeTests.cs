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

    [Fact]
    public void Missing_parts_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => ReadingMerge.ToText(null!));
        Should.Throw<ArgumentNullException>(() => ReadingMerge.Choose(null!));
    }
}
