using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Pasting;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Collect;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Popup;

/// <summary>Ctrl+T and Ctrl+Shift+T: the marked clips become a spreadsheet column or row.</summary>
public sealed class PopupTableTests : IDisposable
{
    private readonly ManualTimeProvider _time = new();
    private readonly FakePasteFlow _paste = new();
    private readonly ClipLibrary _library;
    private readonly SettingsService _settings = new(new FakeSettingsStore());
    private readonly PopupViewModel _vm;

    public PopupTableTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _time), new FakeClipStore());
        _vm = new PopupViewModel(_library, _paste, new FakeQueueControl(), new CollectToggle(new FakeCollectControl(), new FakeNotifier()), _settings, new ImmediateDispatcher(), _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _vm.Dispose();
        _settings.Dispose();
        _library.Dispose();
    }

    private async Task CopyAsync(params string[] texts)
    {
        foreach (var text in texts)
        {
            (await _library.AddAsync(TestClips.Text(text), Ct)).IsSuccess.ShouldBeTrue();
        }
    }

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
    public async Task A_column_puts_each_marked_clip_on_its_own_line_in_marking_order()
    {
        await CopyAsync("10", "20", "30");
        _vm.Open();
        Mark(2, 1, 0);

        await _vm.PasteTableCommand.ExecuteAsync(TableLayout.Column);

        _vm.IsOpen.ShouldBeFalse();
        PastedText().ShouldBe("10\r\n20\r\n30");
    }

    [Fact]
    public async Task A_row_puts_the_marked_clips_side_by_side()
    {
        await CopyAsync("10", "20", "30");
        _vm.Open();
        Mark(2, 1, 0);

        await _vm.PasteTableCommand.ExecuteAsync(TableLayout.Row);

        PastedText().ShouldBe("10\t20\t30");
    }

    [Fact]
    public async Task A_cell_with_a_line_break_or_a_tab_is_quoted_so_it_stays_one_cell()
    {
        await CopyAsync("line one\r\nline two", "say \"hi\"", "a\tb");
        _vm.Open();
        Mark(0, 1, 2);

        await _vm.PasteTableCommand.ExecuteAsync(TableLayout.Column);

        PastedText().ShouldBe("\"a\tb\"\r\n\"say \"\"hi\"\"\"\r\n\"line one\r\nline two\"");
    }

    [Fact]
    public async Task Without_marks_the_highlighted_clip_is_one_cell()
    {
        await CopyAsync("one", "two\r\nlines");
        _vm.Open();

        await _vm.PasteTableCommand.ExecuteAsync(TableLayout.Row);

        PastedText().ShouldBe("\"two\r\nlines\"");
    }

    [Fact]
    public async Task The_separator_does_not_matter_to_a_table()
    {
        await CopyAsync("a", "b");
        await _settings.UpdateAsync(settings => settings with { Separator = SeparatorChoice.Semicolon }, Ct);
        _vm.Open();
        Mark(1, 0);

        await _vm.PasteTableCommand.ExecuteAsync(TableLayout.Column);

        PastedText().ShouldBe("a\r\nb");
    }

    [Fact]
    public async Task An_image_keeps_the_popup_open_and_says_why()
    {
        (await _library.AddAsync(TestClips.Image(7), Ct)).IsSuccess.ShouldBeTrue();
        _vm.Open();

        await _vm.PasteTableCommand.ExecuteAsync(TableLayout.Column);

        _vm.IsOpen.ShouldBeTrue();
        _vm.ErrorMessage.ShouldBe("Images cannot be joined into text. Select only text clips, or paste the image on its own.");
        _paste.PastedContent.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_nothing_listed_or_marked_nothing_happens()
    {
        _vm.Open();

        await _vm.PasteTableCommand.ExecuteAsync(TableLayout.Column);

        _vm.IsOpen.ShouldBeTrue();
        _vm.ErrorMessage.ShouldBeNull();
        _paste.PastedContent.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_footer_offers_the_table_when_clips_are_marked()
    {
        await CopyAsync("a", "b");
        _vm.Open();
        Mark(0, 1);

        _vm.StatusText.ShouldContain("Ctrl+T table");
    }
}
