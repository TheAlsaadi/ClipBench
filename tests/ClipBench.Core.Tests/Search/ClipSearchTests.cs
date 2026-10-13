using ClipBench.Core.Clips;
using ClipBench.Core.Search;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Search;

public class ClipSearchTests
{
    private readonly ClipSearch _search = new();

    private string[] Find(string query, params Clip[] clips) =>
        [.. _search.Search(clips, query).Select(result => result.Clip.Content.PlainText!)];

    // --- empty query ---

    [Fact]
    public void An_empty_query_returns_the_most_recent_clips_in_the_order_given()
    {
        var clips = new[] { TestClips.Text("newest"), TestClips.Text("middle"), TestClips.Text("oldest") };

        var results = _search.Search(clips, "", limit: 2);

        results.Select(result => result.Clip).ShouldBe([clips[0], clips[1]]);
        results.ShouldAllBe(result => result.Score == 0);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    [InlineData("\u0301\u200B")]
    public void A_query_with_nothing_to_match_counts_as_empty(string query)
    {
        var clips = new[] { TestClips.Text("one"), TestClips.Image(1) };

        _search.Search(clips, query).Length.ShouldBe(2);
    }

    // --- substring ---

    [Fact]
    public void Matching_ignores_case()
    {
        Find("HELLO", TestClips.Text("say hello there")).ShouldBe(["say hello there"]);
        Find("hello", TestClips.Text("SAY HELLO THERE")).ShouldBe(["SAY HELLO THERE"]);
    }

    [Fact]
    public void A_clip_without_the_text_is_not_returned()
    {
        Find("zebra", TestClips.Text("apple"), TestClips.Text("banana")).ShouldBeEmpty();
    }

    [Fact]
    public void Line_breaks_and_extra_spaces_do_not_stop_a_match()
    {
        Find("  hello   world ", TestClips.Text("hello\r\nworld")).ShouldBe(["hello\r\nworld"]);
    }

    [Fact]
    public void Rich_text_and_file_clips_are_searched_by_their_text()
    {
        var html = new Clip(ClipId.New(), new HtmlContent("<b>pasta</b>", "pasta"), TestClips.Start);
        var files = TestClips.Files(null, @"C:\Users\me\invoice.pdf");

        _search.Search([html, files], "pasta").Select(result => result.Clip).ShouldBe([html]);
        _search.Search([html, files], "invoice").Select(result => result.Clip).ShouldBe([files]);
    }

    [Fact]
    public void Images_are_only_returned_for_an_empty_query()
    {
        var image = TestClips.Image(1);

        _search.Search([image], "anything").ShouldBeEmpty();
        _search.Search([image], "").Length.ShouldBe(1);
    }

    // --- ranking ---

    [Fact]
    public void Better_matches_rank_higher_whatever_order_the_clips_come_in()
    {
        // Worst first, so the ranking cannot be an accident of the input order.
        var results = Find(
            "foo",
            TestClips.Text("f1o2o3"),       // scattered: fuzzy
            TestClips.Text("xfoox"),        // inside a word: substring
            TestClips.Text("the foo bar"),  // start of a word: word prefix
            TestClips.Text("foobar"),       // start of the text: prefix
            TestClips.Text("foo"));         // whole text: exact

        results.ShouldBe(["foo", "foobar", "the foo bar", "xfoox", "f1o2o3"]);
    }

    [Fact]
    public void Prefix_matches_rank_above_fuzzy_matches()
    {
        Find("gh", TestClips.Text("graph"), TestClips.Text("github"), TestClips.Text("ghost"))
            .ShouldBe(["ghost", "github", "graph"]);
    }

    [Fact]
    public void An_earlier_substring_ranks_above_a_later_one()
    {
        Find("cat", TestClips.Text("xxxxxxxxcat"), TestClips.Text("xcatx")).ShouldBe(["xcatx", "xxxxxxxxcat"]);
    }

    [Fact]
    public void Results_with_equal_rank_keep_the_order_they_were_given_in()
    {
        var clips = new[] { TestClips.Text("apple pie"), TestClips.Text("apple tart"), TestClips.Text("apple juice") };

        _search.Search(clips, "apple").Select(result => result.Clip).ShouldBe(clips);
        _search.Search(clips.Reverse().ToArray(), "apple").Select(result => result.Clip).ShouldBe(clips.Reverse());
    }

    [Fact]
    public void Scores_are_positive_and_never_increase_down_the_list()
    {
        var results = _search.Search([TestClips.Text("f1o2o3"), TestClips.Text("foo"), TestClips.Text("a foo")], "foo");

        results.ShouldAllBe(result => result.Score > 0);
        results.Select(result => result.Score).ShouldBe(results.Select(result => result.Score).OrderDescending());
    }

    // --- fuzzy ---

    [Fact]
    public void Letters_typed_in_order_find_a_word_they_abbreviate()
    {
        Find("pwd", TestClips.Text("my password")).ShouldBe(["my password"]);
    }

    [Fact]
    public void Letters_scattered_far_apart_do_not_count_as_a_match()
    {
        var scattered = "p" + new string('x', 50) + "w" + new string('x', 50) + "d";

        Find("pwd", TestClips.Text(scattered)).ShouldBeEmpty();
    }

    [Fact]
    public void Letters_out_of_order_do_not_match()
    {
        Find("dwp", TestClips.Text("password")).ShouldBeEmpty();
    }

    [Fact]
    public void A_single_letter_never_matches_fuzzily()
    {
        Find("q", TestClips.Text("abc")).ShouldBeEmpty();
    }

    [Fact]
    public void A_long_query_is_matched_as_text_only()
    {
        var query = new string('a', 40);

        Find(query, TestClips.Text("a" + string.Join("b", Enumerable.Repeat("a", 39)))).ShouldBeEmpty();
        Find(query, TestClips.Text("x" + query + "x")).Length.ShouldBe(1);
    }

    // --- languages and symbols ---

    [Fact]
    public void Arabic_text_is_found_by_a_part_of_it()
    {
        const string clip = "\u0645\u0631\u062D\u0628\u0627 \u0628\u0627\u0644\u0639\u0627\u0644\u0645";

        Find("\u0628\u0627\u0644\u0639\u0627\u0644\u0645", TestClips.Text(clip)).ShouldBe([clip]);
        Find("\u0645\u0631\u062D", TestClips.Text(clip)).ShouldBe([clip]);
    }

    [Fact]
    public void Arabic_marks_do_not_get_in_the_way_in_either_direction()
    {
        const string withMarks = "\u0645\u064E\u0631\u0652\u062D\u064E\u0628\u064B\u0627";
        const string plain = "\u0645\u0631\u062D\u0628\u0627";

        Find(plain, TestClips.Text(withMarks)).ShouldBe([withMarks]);
        Find(withMarks, TestClips.Text(plain)).ShouldBe([plain]);
    }

    [Fact]
    public void Arabic_letters_stretched_with_a_tatweel_still_match()
    {
        const string stretched = "\u0645\u0631\u062D\u0628\u0640\u0640\u0627";

        Find("\u0645\u0631\u062D\u0628\u0627", TestClips.Text(stretched)).ShouldBe([stretched]);
    }

    [Fact]
    public void The_alef_forms_match_each_other()
    {
        // Ahmad written with a hamza above the alef is found by the plain alef.
        const string withHamza = "\u0623\u062D\u0645\u062F";

        Find("\u0627\u062D\u0645\u062F", TestClips.Text(withHamza)).ShouldBe([withHamza]);
    }

    [Fact]
    public void Arabic_indic_and_western_digits_match_each_other()
    {
        const string arabicDigits = "\u0631\u0642\u0645 \u0661\u0662\u0663";

        Find("123", TestClips.Text(arabicDigits)).ShouldBe([arabicDigits]);
        Find("\u0661\u0662\u0663", TestClips.Text("order 123")).ShouldBe(["order 123"]);
    }

    [Fact]
    public void Hebrew_text_is_found_with_or_without_vowel_points()
    {
        const string plain = "\u05E9\u05DC\u05D5\u05DD \u05E2\u05D5\u05DC\u05DD";
        const string pointed = "\u05E9\u05B8\u05C1\u05DC\u05D5\u05B9\u05DD";

        Find("\u05E2\u05D5\u05DC\u05DD", TestClips.Text(plain)).ShouldBe([plain]);
        Find("\u05E9\u05DC\u05D5\u05DD", TestClips.Text(pointed)).ShouldBe([pointed]);
    }

    [Fact]
    public void Latin_accents_do_not_get_in_the_way()
    {
        Find("cafe", TestClips.Text("Caf\u00E9 au lait")).ShouldBe(["Caf\u00E9 au lait"]);
        Find("caf\u00E9", TestClips.Text("cafe")).ShouldBe(["cafe"]);
        Find("ecole", TestClips.Text("\u00C9COLE")).ShouldBe(["\u00C9COLE"]);
    }

    [Fact]
    public void Emoji_are_found_whole_and_without_the_presentation_selector()
    {
        const string text = "I \u2764\uFE0F pizza \U0001F355";

        Find("\U0001F355", TestClips.Text(text)).ShouldBe([text]);
        Find("\u2764", TestClips.Text(text)).ShouldBe([text]);
        Find("pizza", TestClips.Text(text)).ShouldBe([text]);
    }

    [Fact]
    public void Malformed_text_neither_crashes_the_search_nor_hides_the_rest_of_the_clip()
    {
        // A lone surrogate is invalid UTF-16 and would make normalization throw.
        Find("ab", TestClips.Text("ab\uD83Dcd")).Length.ShouldBe(1);
        Find("cd", TestClips.Text("ab\uDE00cd")).Length.ShouldBe(1);
        Find("a\uD83Db", TestClips.Text("anything")).ShouldBeEmpty();
    }

    // --- limits ---

    [Fact]
    public void The_limit_caps_the_number_of_results()
    {
        var clips = Enumerable.Range(0, 20).Select(i => TestClips.Text($"item {i}")).ToArray();

        _search.Search(clips, "item", limit: 5).Length.ShouldBe(5);
        _search.Search(clips, "item").Length.ShouldBe(20);
    }

    [Fact]
    public void Only_the_start_of_a_very_long_text_is_searched()
    {
        var early = TestClips.Text("needle" + new string('x', 100_000));
        var late = TestClips.Text(new string('x', 100_000) + "needle");

        var results = _search.Search([early, late], "needle");

        results.Select(result => result.Clip).ShouldBe([early]);
    }

    [Fact]
    public void An_enormous_query_is_cut_instead_of_crashing()
    {
        Should.NotThrow(() => _search.Search([TestClips.Text("abc")], new string('a', 1_000_000)));
    }

    [Fact]
    public void Searching_twice_gives_the_same_answer()
    {
        var clips = new[] { TestClips.Text("Alpha beta"), TestClips.Text("gamma") };

        var first = _search.Search(clips, "beta");
        var second = _search.Search(clips, "beta");

        second.ShouldBe(first);
    }

    [Fact]
    public void A_limit_below_one_and_missing_arguments_are_bugs()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _search.Search([], "x", limit: 0));
        Should.Throw<ArgumentNullException>(() => _search.Search(null!, "x"));
        Should.Throw<ArgumentNullException>(() => _search.Search([], null!));
    }
}
