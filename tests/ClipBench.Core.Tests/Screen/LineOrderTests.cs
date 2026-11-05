using ClipBench.Core.Screen;

namespace ClipBench.Core.Tests.Screen;

public class LineOrderTests
{
    // Arabic written out by code point so no file encoding can change it.
    private static string Mohammed => Word(0x645, 0x62D, 0x645, 0x62F);

    private static string Alsaadi => Word(0x627, 0x644, 0x635, 0x627, 0x639, 0x62F, 0x64A);

    private static string Marhaba => Word(0x645, 0x631, 0x62D, 0x628, 0x627);

    private static string Bikum => Word(0x628, 0x643, 0x645);

    private static string Fi => Word(0x641, 0x64A);

    private static string Rakam => Word(0x631, 0x642, 0x645);

    private static string Company => Word(0x644, 0x644, 0x634, 0x631, 0x643, 0x629);

    private static string Word(params int[] codePoints) => string.Concat(codePoints.Select(cp => (char)cp));

    private static string Logical(params string[] wordsLeftToRight) => LineOrder.ToLogical(wordsLeftToRight);

    // --- the case that was wrong: Windows lists words in the order they appear on the screen, left to right ---

    [Fact]
    public void Two_Arabic_words_come_back_in_reading_order_the_first_name_before_the_family_name()
    {
        // On screen the first word is at the right, so Windows lists the family name first.
        Logical(Alsaadi, Mohammed).ShouldBe(Mohammed + " " + Alsaadi);
    }

    [Fact]
    public void A_line_of_Arabic_words_is_reversed_as_a_whole()
    {
        Logical(Fi, Bikum, Marhaba).ShouldBe(Marhaba + " " + Bikum + " " + Fi);
    }

    [Fact]
    public void A_number_between_Arabic_words_keeps_its_place_in_the_sentence()
    {
        // Logical: Rakam 2026 Company. On screen: Company 2026 Rakam.
        Logical(Company, "2026", Rakam).ShouldBe(Rakam + " 2026 " + Company);
    }

    [Fact]
    public void English_inside_an_Arabic_sentence_keeps_its_words_in_order_and_its_place()
    {
        // Logical: Marhaba Bikum Fi ClipBench 2026. On screen the English is at the left end.
        Logical("ClipBench", "2026", Fi, Bikum, Marhaba)
            .ShouldBe(Marhaba + " " + Bikum + " " + Fi + " ClipBench 2026");
    }

    [Fact]
    public void English_with_a_few_Arabic_words_stays_a_left_to_right_line_and_only_the_Arabic_run_is_reversed()
    {
        // Logical: Hello there Mohammed Alsaadi friend. On screen the Arabic run reads right to left.
        Logical("Hello", "there", Alsaadi, Mohammed, "friend")
            .ShouldBe("Hello there " + Mohammed + " " + Alsaadi + " friend");
    }

    [Fact]
    public void Two_separate_Arabic_runs_in_an_English_line_are_each_reversed_in_place()
    {
        Logical(Alsaadi, Mohammed, "and", "then", "also", Bikum, Marhaba, "ok", "now")
            .ShouldBe(Mohammed + " " + Alsaadi + " and then also " + Marhaba + " " + Bikum + " ok now");
    }

    // --- nothing to reorder ---

    [Fact]
    public void A_line_with_no_right_to_left_letters_is_left_exactly_as_it_is()
    {
        Logical("Invoice", "2026-10", "for", "ACME", "Corp").ShouldBe("Invoice 2026-10 for ACME Corp");
        Logical("if", "(a", ">", "b)", "{", "}").ShouldBe("if (a > b) { }");
    }

    [Fact]
    public void One_Arabic_word_is_unchanged()
    {
        Logical(Mohammed).ShouldBe(Mohammed);
    }

    [Fact]
    public void A_tie_between_the_two_directions_is_read_left_to_right()
    {
        Logical("Name", Mohammed).ShouldBe("Name " + Mohammed);
    }

    [Fact]
    public void Empty_words_and_no_words_give_empty_text()
    {
        Logical().ShouldBeEmpty();
        Logical(string.Empty, "  ").ShouldBeEmpty();
        Logical(Alsaadi, string.Empty, Mohammed).ShouldBe(Mohammed + " " + Alsaadi);
    }

    [Fact]
    public void A_word_that_is_only_punctuation_joins_the_run_around_it()
    {
        Logical(Alsaadi, "-", Mohammed).ShouldBe(Mohammed + " - " + Alsaadi);
    }

    // --- telling whether a line needs it ---

    [Theory]
    [InlineData("Invoice 2026", false)]
    [InlineData("", false)]
    [InlineData("日本語のテキスト", false)]
    public void Text_without_right_to_left_letters_is_not_reordered(string text, bool expected)
    {
        LineOrder.ContainsRightToLeft(text).ShouldBe(expected);
    }

    [Fact]
    public void Arabic_Hebrew_and_other_right_to_left_scripts_are_recognised()
    {
        LineOrder.ContainsRightToLeft(Mohammed).ShouldBeTrue();
        LineOrder.ContainsRightToLeft("abc " + Mohammed).ShouldBeTrue();
        LineOrder.ContainsRightToLeft(Word(0x5E9, 0x5DC, 0x5D5, 0x5DD)).ShouldBeTrue(); // Hebrew
        LineOrder.ContainsRightToLeft(Word(0x710, 0x72C)).ShouldBeTrue(); // Syriac
        LineOrder.ContainsRightToLeft(Word(0xFE8D)).ShouldBeTrue(); // Arabic presentation form
        LineOrder.ContainsRightToLeft(Word(0x661, 0x662)).ShouldBeFalse(); // Arabic-Indic digits alone are numbers
    }

    [Fact]
    public void A_missing_list_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => LineOrder.ToLogical(null!));
        Should.Throw<ArgumentNullException>(() => LineOrder.ContainsRightToLeft(null!));
    }
}
