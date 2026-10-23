using ClipBench.Core.Clips;
using ClipBench.Core.History;
using ClipBench.Core.Settings;
using ClipBench.Presentation.Collect;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Rows;
using ClipBench.Presentation.Tests.Support;

namespace ClipBench.Presentation.Tests.Popup;

/// <summary>Shift+Enter: the transform picker, and pasting the changed text.</summary>
public sealed class PopupTransformTests : IDisposable
{
    private const string Jwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

    private readonly ManualTimeProvider _time = new();
    private readonly FakePasteFlow _paste = new();
    private readonly ClipLibrary _library;
    private readonly SettingsService _settings = new(new FakeSettingsStore());
    private readonly PopupViewModel _vm;

    public PopupTransformTests()
    {
        _library = new ClipLibrary(new ClipHistory(new HistoryOptions(), _time), new FakeClipStore());
        _vm = new PopupViewModel(_library, _paste, new FakeQueueControl(), new CollectToggle(new FakeCollectControl(), new FakeNotifier()), TestPickers.New(), TestSidebars.New(_library), TestSnippets.New(), _settings, new ImmediateDispatcher(), _time);
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

    private void Mark(params int[] positions)
    {
        foreach (var position in positions)
        {
            _vm.SelectedIndex = position;
            _vm.ToggleMarkCommand.Execute(null);
        }
    }

    private void Choose(string name)
    {
        var index = _vm.Transforms.Items.ToList().FindIndex(item => item.Name == name);
        index.ShouldBeGreaterThanOrEqualTo(0, $"no transform named {name}");
        _vm.Transforms.SelectedIndex = index;
    }

    private string PastedText() => ((TextContent)_paste.PastedContent.Single()).Text;

    [Fact]
    public async Task Shift_Enter_opens_the_picker_on_the_highlighted_clip_and_Enter_pastes_the_changed_text()
    {
        await AddAsync(TestClips.Text("hello"));
        _vm.Open();

        _vm.OpenTransformsCommand.Execute(null);
        _vm.IsTransformsOpen.ShouldBeTrue();
        _vm.Transforms.IsOpen.ShouldBeTrue();
        _vm.IsOpen.ShouldBeTrue("the popup stays open behind the picker");

        Choose("UPPER CASE");
        await _vm.ConfirmTransformCommand.ExecuteAsync(null);

        PastedText().ShouldBe("HELLO");
        _paste.Pasted.ShouldBeEmpty("the stored clip is never pasted instead");
        _vm.IsOpen.ShouldBeFalse();
        _vm.IsTransformsOpen.ShouldBeFalse();
        _library.Clips.Single().Content.PlainText.ShouldBe("hello", "a transform never changes the stored clip");
    }

    [Fact]
    public async Task The_popup_is_hidden_before_the_changed_text_is_pasted()
    {
        await AddAsync(TestClips.Text("hello"));
        _vm.Open();
        var closedBeforePaste = false;
        _vm.CloseRequested += (_, _) => closedBeforePaste = _paste.PastedContent.Count == 0;
        _vm.OpenTransformsCommand.Execute(null);

        await _vm.ConfirmTransformCommand.ExecuteAsync(null);

        closedBeforePaste.ShouldBeTrue();
    }

    [Fact]
    public async Task With_marks_the_picker_works_on_the_marked_clips_joined_in_the_order_they_were_marked()
    {
        await AddAsync(TestClips.Text("first"), TestClips.Text("second"));
        _vm.Open();
        Mark(1, 0);

        _vm.OpenTransformsCommand.Execute(null);
        Choose("Remove line breaks");
        await _vm.ConfirmTransformCommand.ExecuteAsync(null);

        PastedText().ShouldBe("first second");
    }

    [Fact]
    public async Task The_separator_in_the_settings_joins_the_marked_clips_before_they_are_changed()
    {
        await AddAsync(TestClips.Text("a"), TestClips.Text("b"));
        await _settings.UpdateAsync(settings => settings with { Separator = SeparatorChoice.Comma }, Ct);
        _vm.Open();
        Mark(1, 0);

        _vm.OpenTransformsCommand.Execute(null);
        Choose("UPPER CASE");
        await _vm.ConfirmTransformCommand.ExecuteAsync(null);

        PastedText().ShouldBe("A,B");
    }

    [Fact]
    public async Task A_clip_of_files_is_changed_as_its_list_of_paths()
    {
        await AddAsync(TestClips.Files(paths: [@"C:\Users\me\a.txt", @"C:\Users\me\b.txt"]));
        _vm.Open();

        _vm.OpenTransformsCommand.Execute(null);
        Choose("Remove line breaks");
        await _vm.ConfirmTransformCommand.ExecuteAsync(null);

        PastedText().ShouldBe(@"C:\Users\me\a.txt C:\Users\me\b.txt");
    }

    [Fact]
    public async Task An_image_cannot_be_changed_and_the_popup_says_so()
    {
        await AddAsync(TestClips.Image(1));
        _vm.Open();

        _vm.OpenTransformsCommand.Execute(null);

        _vm.IsTransformsOpen.ShouldBeFalse();
        _vm.ErrorMessage.ShouldNotBeNull();
        _vm.ErrorMessage.ShouldContain("image");
        _vm.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task Marked_clips_that_include_an_image_cannot_be_changed_and_the_popup_says_why()
    {
        await AddAsync(TestClips.Text("text"), TestClips.Image(1));
        _vm.Open();
        Mark(0, 1);

        _vm.OpenTransformsCommand.Execute(null);

        _vm.IsTransformsOpen.ShouldBeFalse();
        _vm.ErrorMessage.ShouldNotBeNull();
    }

    [Fact]
    public void With_nothing_to_change_the_picker_stays_closed()
    {
        _vm.Open();

        _vm.OpenTransformsCommand.Execute(null);

        _vm.IsTransformsOpen.ShouldBeFalse();
        _vm.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task A_secret_is_hidden_in_the_preview_but_still_changed_when_pasted()
    {
        await AddAsync(TestClips.Text(Jwt));
        _vm.Open();

        _vm.OpenTransformsCommand.Execute(null);
        Choose("lower case");
        _vm.Transforms.Preview.ShouldBe(ClipRowViewModel.Mask);
        await _vm.ConfirmTransformCommand.ExecuteAsync(null);

        PastedText().ShouldBe(Jwt.ToLowerInvariant());
    }

    [Fact]
    public async Task A_transform_that_cannot_work_keeps_both_open_with_the_reason_and_pastes_nothing()
    {
        await AddAsync(TestClips.Text("this is not json"));
        _vm.Open();
        _vm.OpenTransformsCommand.Execute(null);
        Choose("JSON format");

        await _vm.ConfirmTransformCommand.ExecuteAsync(null);

        _paste.PastedContent.ShouldBeEmpty();
        _vm.IsOpen.ShouldBeTrue();
        _vm.IsTransformsOpen.ShouldBeTrue();
        _vm.Transforms.ErrorMessage.ShouldStartWith("JSON format: ");
    }

    [Fact]
    public async Task Find_and_replace_asks_first_and_then_pastes_the_result()
    {
        await AddAsync(TestClips.Text("one two one"));
        _vm.Open();
        _vm.OpenTransformsCommand.Execute(null);
        Choose("Find and replace\u2026");

        await _vm.ConfirmTransformCommand.ExecuteAsync(null);
        _paste.PastedContent.ShouldBeEmpty();
        _vm.Transforms.IsAskingFindReplace.ShouldBeTrue();

        _vm.Transforms.FindText = "one";
        _vm.Transforms.ReplaceText = "1";
        await _vm.SubmitFindReplaceCommand.ExecuteAsync(null);

        PastedText().ShouldBe("1 two 1");
        _vm.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task A_find_and_replace_that_is_not_valid_keeps_asking()
    {
        await AddAsync(TestClips.Text("x"));
        _vm.Open();
        _vm.OpenTransformsCommand.Execute(null);
        Choose("Find and replace\u2026");
        await _vm.ConfirmTransformCommand.ExecuteAsync(null);

        await _vm.SubmitFindReplaceCommand.ExecuteAsync(null);

        _paste.PastedContent.ShouldBeEmpty();
        _vm.Transforms.IsAskingFindReplace.ShouldBeTrue();
        _vm.Transforms.ErrorMessage.ShouldBe("Enter the text to find.");
    }

    [Fact]
    public async Task Esc_closes_the_picker_first_and_the_popup_on_the_next_press()
    {
        await AddAsync(TestClips.Text("hello"));
        _vm.Open();
        _vm.OpenTransformsCommand.Execute(null);

        _vm.EscapeCommand.Execute(null);
        _vm.IsTransformsOpen.ShouldBeFalse();
        _vm.IsOpen.ShouldBeTrue();

        _vm.EscapeCommand.Execute(null);
        _vm.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task Esc_in_the_find_and_replace_fields_goes_back_to_the_list()
    {
        await AddAsync(TestClips.Text("hello"));
        _vm.Open();
        _vm.OpenTransformsCommand.Execute(null);
        Choose("Find and replace\u2026");
        await _vm.ConfirmTransformCommand.ExecuteAsync(null);

        _vm.EscapeCommand.Execute(null);

        _vm.Transforms.IsAskingFindReplace.ShouldBeFalse();
        _vm.IsTransformsOpen.ShouldBeTrue();
        _vm.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task Closing_the_popup_closes_the_picker_too()
    {
        await AddAsync(TestClips.Text("hello"));
        _vm.Open();
        _vm.OpenTransformsCommand.Execute(null);

        _vm.Close();

        _vm.IsTransformsOpen.ShouldBeFalse();
        _vm.Transforms.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_status_line_shows_the_picker_hints_while_it_is_open()
    {
        await AddAsync(TestClips.Text("hello"));
        _vm.Open();
        var normal = _vm.StatusText;
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _vm.OpenTransformsCommand.Execute(null);

        _vm.StatusText.ShouldBe(_vm.Transforms.StatusText);
        changes.ShouldContain(nameof(PopupViewModel.StatusText));
        changes.ShouldContain(nameof(PopupViewModel.IsTransformsOpen));

        _vm.EscapeCommand.Execute(null);
        _vm.StatusText.ShouldBe(normal);
    }

    [Fact]
    public async Task The_list_behind_the_picker_cannot_be_used_while_it_is_open()
    {
        await AddAsync(TestClips.Text("hello"));
        _vm.Open();
        _vm.IsMainEnabled.ShouldBeTrue();
        var changes = new List<string?>();
        _vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _vm.OpenTransformsCommand.Execute(null);
        _vm.IsMainEnabled.ShouldBeFalse();
        changes.ShouldContain(nameof(PopupViewModel.IsMainEnabled));

        _vm.EscapeCommand.Execute(null);
        _vm.IsMainEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Reopening_the_popup_does_not_bring_back_an_old_picker()
    {
        await AddAsync(TestClips.Text("hello"));
        _vm.Open();
        _vm.OpenTransformsCommand.Execute(null);
        _vm.Close();

        _vm.Open();

        _vm.IsTransformsOpen.ShouldBeFalse();
    }

    [Fact]
    public void A_missing_picker_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => new PopupViewModel(_library, _paste, new FakeQueueControl(), new CollectToggle(new FakeCollectControl(), new FakeNotifier()), null!, TestSidebars.New(_library), TestSnippets.New(), _settings, new ImmediateDispatcher(), _time));
    }
}
