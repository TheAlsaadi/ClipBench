using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Popup;

/// <summary>Enter with clips marked: they are joined and pasted as one text.</summary>
public sealed class PopupBatchTests : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly FakePasteFlow _paste = new();
    private readonly ClipLibrary _library;
    private readonly SettingsService _settings = new(new FakeSettingsStore());
    private readonly PopupViewModel _vm;

    public PopupBatchTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _time), new FakeClipStore());
        _vm = new PopupViewModel(_library, _paste, _settings, new ImmediateDispatcher(), _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _vm.Dispose();
        _settings.Dispose();
        _library.Dispose();
    }

    private async Task AddAsync(params Clip[] clips)
    {
        foreach (var clip in clips)
        {
            (await _library.AddAsync(clip, Ct)).IsSuccess.ShouldBeTrue();
        }
    }

    /// <summary>Marks the rows at these positions, in this order.</summary>
    private void Mark(params int[] positions)
    {
        foreach (var position in positions)
        {
            _vm.SelectedIndex = position;
            _vm.ToggleMarkCommand.Execute(null);
        }
    }

    private string PastedText() => ((TextContent)_paste.PastedContent.Single()).Text;

    [Fact]
    public async Task Enter_with_marks_hides_the_popup_then_pastes_them_joined_in_the_order_they_were_marked()
    {
        await AddAsync(TestClips.Text("a@example.com"), TestClips.Text("b@example.com"), TestClips.Text("c@example.com"));
        _vm.Open();
        var closedBeforePaste = false;
        _vm.CloseRequested += (_, _) => closedBeforePaste = _paste.PastedContent.Count == 0;
        Mark(2, 0, 1);

        await _vm.PasteCommand.ExecuteAsync(null);

        closedBeforePaste.ShouldBeTrue();
        _vm.IsOpen.ShouldBeFalse();
        PastedText().ShouldBe("a@example.com\r\nc@example.com\r\nb@example.com");
        _paste.Pasted.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_separator_in_the_settings_joins_the_clips()
    {
        await AddAsync(TestClips.Text("one"), TestClips.Text("two"));
        await _settings.UpdateAsync(settings => settings with { Separator = SeparatorChoice.CommaSpace }, Ct);
        _vm.Open();
        Mark(1, 0);

        await _vm.PasteCommand.ExecuteAsync(null);

        PastedText().ShouldBe("one, two");
    }

    [Fact]
    public async Task A_custom_separator_joins_the_clips()
    {
        await AddAsync(TestClips.Text("one"), TestClips.Text("two"));
        await _settings.UpdateAsync(settings => settings with { Separator = SeparatorChoice.Custom, CustomSeparator = @" |\t" }, Ct);
        _vm.Open();
        Mark(1, 0);

        await _vm.PasteCommand.ExecuteAsync(null);

        PastedText().ShouldBe("one |\ttwo");
    }

    [Fact]
    public async Task Marked_clips_the_search_hides_are_pasted_too()
    {
        await AddAsync(TestClips.Text("apple"), TestClips.Text("banana"));
        _vm.Open();
        Mark(0);
        _vm.SearchText = "app";
        _time.FireNext().ShouldBeTrue();
        Mark(0);

        await _vm.PasteCommand.ExecuteAsync(null);

        PastedText().ShouldBe("banana\r\napple");
    }

    [Fact]
    public async Task One_mark_pastes_that_clip_as_it_is_even_when_another_row_is_highlighted()
    {
        var html = new Clip(ClipId.New(), new HtmlContent("<b>bold</b>", "bold"), TestClips.Start);
        await AddAsync(html, TestClips.Text("plain"));
        _vm.Open();
        Mark(1);
        _vm.SelectedIndex = 0;

        await _vm.PasteCommand.ExecuteAsync(null);

        _paste.Pasted.ShouldBe([html]);
        _paste.PastedContent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rich_text_and_file_lists_are_joined_by_their_plain_text()
    {
        var html = new Clip(ClipId.New(), new HtmlContent("<b>bold</b>", "bold"), TestClips.Start);
        await AddAsync(html, TestClips.Files(null, @"C:\a.txt", @"C:\b.txt"));
        _vm.Open();
        Mark(1, 0);

        await _vm.PasteCommand.ExecuteAsync(null);

        PastedText().ShouldBe("bold\r\nC:\\a.txt\r\nC:\\b.txt");
    }

    [Fact]
    public async Task An_image_among_the_marks_keeps_the_popup_open_and_says_why()
    {
        await AddAsync(TestClips.Image(1), TestClips.Text("words"));
        _vm.Open();
        Mark(0, 1);

        await _vm.PasteCommand.ExecuteAsync(null);

        _vm.IsOpen.ShouldBeTrue();
        _vm.ErrorMessage.ShouldBe("Images cannot be joined into text. Select only text clips, or paste the image on its own.");
        _paste.PastedContent.ShouldBeEmpty();
        _paste.Pasted.ShouldBeEmpty();
        _vm.MarkedCount.ShouldBe(2);
    }

    [Fact]
    public async Task Marks_too_large_to_join_keep_the_popup_open_and_say_why()
    {
        var half = new string('x', (Core.Pasting.PasteComposer.DefaultMaxOutputCharacters / 2) + 1);
        await AddAsync(TestClips.Text(half + "a"), TestClips.Text(half + "b"));
        _vm.Open();
        Mark(0, 1);

        await _vm.PasteCommand.ExecuteAsync(null);

        _vm.IsOpen.ShouldBeTrue();
        _vm.ErrorMessage.ShouldBe("The combined text is too large to paste.");
        _paste.PastedContent.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_footer_says_how_many_are_marked_and_which_separator_joins_them()
    {
        await AddAsync(TestClips.Text("one"), TestClips.Text("two"));
        _vm.Open();
        Mark(0, 1);

        _vm.StatusText.ShouldStartWith("Enter paste 2 · Ctrl+S New line");
    }

    [Fact]
    public async Task The_footer_follows_a_change_of_separator()
    {
        await AddAsync(TestClips.Text("one"), TestClips.Text("two"));
        _vm.Open();
        Mark(0, 1);
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await _vm.NextSeparatorCommand.ExecuteAsync(null);

        changed.ShouldContain(nameof(PopupViewModel.StatusText));
        _vm.StatusText.ShouldStartWith("Enter paste 2 · Ctrl+S Blank line");
    }

    [Fact]
    public async Task Without_marks_the_footer_offers_marking()
    {
        await AddAsync(TestClips.Text("one"));
        _vm.Open();

        _vm.StatusText.ShouldBe("Enter paste · Space select · Ctrl+P pin · Del delete · Esc close");
    }
}
