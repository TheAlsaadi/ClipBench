using System.Text;
using System.Text.Json;
using ClipBench.Core.Results;
using ClipBench.Core.Settings;
using ClipBench.Storage.Settings;
using ClipBench.Storage.Tests.Support;

namespace ClipBench.Storage.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private static readonly AppSettings _changed = AppSettings.Default with
    {
        Separator = SeparatorChoice.Custom,
        CustomSeparator = @" | \t",
        RestorePreviousClipboard = false,
        KeepCopiesWhileCollecting = true,
    };

    private readonly TempDirectory _folder = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string SettingsPath => _folder.File("settings.json");

    private string BadPath => _folder.File("settings.json.bad");

    private JsonSettingsStore Create() => new(SettingsPath);

    public void Dispose() => _folder.Dispose();

    // --- loading ---

    [Fact]
    public async Task Without_a_file_the_defaults_are_used()
    {
        var load = await Create().LoadAsync(Ct);

        load.ShouldBe(new SettingsLoad(AppSettings.Default, SettingsLoadStatus.Missing));
    }

    [Fact]
    public async Task Saved_settings_load_back_the_same()
    {
        var store = Create();

        (await store.SaveAsync(_changed, Ct)).IsSuccess.ShouldBeTrue();
        var load = await Create().LoadAsync(Ct);

        load.ShouldBe(new SettingsLoad(_changed, SettingsLoadStatus.Loaded));
    }

    [Theory]
    [InlineData(SeparatorChoice.NewLine)]
    [InlineData(SeparatorChoice.BlankLine)]
    [InlineData(SeparatorChoice.CommaSpace)]
    [InlineData(SeparatorChoice.Comma)]
    [InlineData(SeparatorChoice.Semicolon)]
    [InlineData(SeparatorChoice.Tab)]
    [InlineData(SeparatorChoice.Space)]
    [InlineData(SeparatorChoice.None)]
    [InlineData(SeparatorChoice.Custom)]
    public async Task Every_separator_choice_loads_back_the_same(SeparatorChoice choice)
    {
        var settings = AppSettings.Default with { Separator = choice };
        await Create().SaveAsync(settings, Ct);

        (await Create().LoadAsync(Ct)).Settings.ShouldBe(settings);
    }

    [Fact]
    public async Task The_file_is_readable_json_with_the_choice_written_by_name()
    {
        await Create().SaveAsync(AppSettings.Default with { Separator = SeparatorChoice.CommaSpace }, Ct);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath, Ct));
        var root = json.RootElement;
        root.GetProperty("version").GetInt32().ShouldBe(1);
        root.GetProperty("separator").GetString().ShouldBe("CommaSpace");
        root.GetProperty("customSeparator").GetString().ShouldBe(string.Empty);
        root.GetProperty("restorePreviousClipboard").GetBoolean().ShouldBeTrue();
        root.GetProperty("keepCopiesWhileCollecting").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Keys_it_does_not_know_are_ignored()
    {
        await WriteAsync("""{ "separator": "Tab", "theme": "dark", "nested": { "a": [1, 2, 3] } }""");

        var load = await Create().LoadAsync(Ct);

        load.ShouldBe(new SettingsLoad(AppSettings.Default with { Separator = SeparatorChoice.Tab }, SettingsLoadStatus.Loaded));
    }

    [Fact]
    public async Task Missing_keys_keep_their_defaults()
    {
        await WriteAsync("""{ "restorePreviousClipboard": false }""");

        (await Create().LoadAsync(Ct)).Settings.ShouldBe(AppSettings.Default with { RestorePreviousClipboard = false });
    }

    [Theory]
    [InlineData("""{ "separator": 3 }""")]
    [InlineData("""{ "separator": "3" }""")]
    [InlineData("""{ "separator": "Pipe" }""")]
    [InlineData("""{ "separator": null }""")]
    [InlineData("""{ "customSeparator": 7 }""")]
    [InlineData("""{ "restorePreviousClipboard": "no" }""")]
    [InlineData("""{ "keepCopiesWhileCollecting": 1 }""")]
    public async Task A_key_with_a_value_it_cannot_use_keeps_its_default(string json)
    {
        await WriteAsync(json);

        (await Create().LoadAsync(Ct)).ShouldBe(new SettingsLoad(AppSettings.Default, SettingsLoadStatus.Loaded));
    }

    [Fact]
    public async Task Separator_names_are_matched_exactly()
    {
        await WriteAsync("""{ "separator": "comma" }""");

        (await Create().LoadAsync(Ct)).Settings.Separator.ShouldBe(SeparatorChoice.NewLine);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("")]
    [InlineData("{ \"separator\": \"Tab\" } { }")]
    public async Task A_damaged_file_gives_the_defaults_and_is_set_aside(string content)
    {
        await WriteAsync(content);

        var load = await Create().LoadAsync(Ct);

        load.ShouldBe(new SettingsLoad(AppSettings.Default, SettingsLoadStatus.Damaged));
        File.Exists(SettingsPath).ShouldBeFalse();
        (await File.ReadAllTextAsync(BadPath, Ct)).ShouldBe(content);
    }

    [Fact]
    public async Task A_file_nested_too_deeply_is_damaged()
    {
        var deep = "{ \"a\": " + string.Concat(Enumerable.Repeat("[", 200)) + string.Concat(Enumerable.Repeat("]", 200)) + " }";
        await WriteAsync(deep);

        (await Create().LoadAsync(Ct)).Status.ShouldBe(SettingsLoadStatus.Damaged);
    }

    [Fact]
    public async Task A_file_larger_than_the_limit_is_damaged_even_when_it_is_valid_json()
    {
        await WriteAsync("{ \"separator\": \"Tab\" }" + new string(' ', JsonSettingsStore.MaxFileBytes));

        var load = await Create().LoadAsync(Ct);

        load.ShouldBe(new SettingsLoad(AppSettings.Default, SettingsLoadStatus.Damaged));
        File.Exists(BadPath).ShouldBeTrue();
    }

    [Fact]
    public async Task A_file_exactly_at_the_limit_is_read()
    {
        var json = "{ \"separator\": \"Tab\" }";
        await WriteAsync(json + new string(' ', JsonSettingsStore.MaxFileBytes - json.Length));

        (await Create().LoadAsync(Ct)).Settings.Separator.ShouldBe(SeparatorChoice.Tab);
    }

    [Fact]
    public async Task A_newer_damaged_file_replaces_an_older_one_set_aside()
    {
        await File.WriteAllTextAsync(BadPath, "older", Ct);
        await WriteAsync("newer {");

        await Create().LoadAsync(Ct);

        (await File.ReadAllTextAsync(BadPath, Ct)).ShouldBe("newer {");
    }

    [Fact]
    public async Task A_file_another_program_holds_gives_the_defaults_and_is_left_alone()
    {
        await WriteAsync("""{ "separator": "Tab" }""");

        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var load = await Create().LoadAsync(Ct);

            load.ShouldBe(new SettingsLoad(AppSettings.Default, SettingsLoadStatus.Unreadable));
        }

        File.Exists(BadPath).ShouldBeFalse();
        (await Create().LoadAsync(Ct)).Settings.Separator.ShouldBe(SeparatorChoice.Tab);
    }

    [Fact]
    public async Task A_byte_order_mark_is_accepted()
    {
        await File.WriteAllBytesAsync(SettingsPath, [.. Encoding.UTF8.GetPreamble(), .. """{ "separator": "Space" }"""u8.ToArray()], Ct);

        (await Create().LoadAsync(Ct)).Settings.Separator.ShouldBe(SeparatorChoice.Space);
    }

    // --- saving ---

    [Fact]
    public async Task Saving_creates_the_folder_when_it_is_missing()
    {
        var path = Path.Combine(_folder.Path, "new", "settings.json");

        (await new JsonSettingsStore(path).SaveAsync(_changed, Ct)).IsSuccess.ShouldBeTrue();

        (await new JsonSettingsStore(path).LoadAsync(Ct)).Settings.ShouldBe(_changed);
    }

    [Fact]
    public async Task Saving_replaces_what_was_saved_and_leaves_no_temporary_file()
    {
        var store = Create();

        await store.SaveAsync(_changed, Ct);
        await store.SaveAsync(AppSettings.Default, Ct);

        (await Create().LoadAsync(Ct)).Settings.ShouldBe(AppSettings.Default);
        Directory.GetFiles(_folder.Path).Select(Path.GetFileName).ShouldBe(["settings.json"]);
    }

    [Fact]
    public async Task A_save_that_cannot_be_written_is_a_storage_failure_and_does_not_throw()
    {
        Directory.CreateDirectory(SettingsPath);

        var result = await Create().SaveAsync(_changed, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.StorageFailure);
        Directory.GetFiles(_folder.Path).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_save_into_a_held_file_fails_and_keeps_the_old_settings()
    {
        var store = Create();
        await store.SaveAsync(_changed, Ct);

        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            (await store.SaveAsync(AppSettings.Default, Ct)).IsFailure.ShouldBeTrue();
        }

        (await Create().LoadAsync(Ct)).Settings.ShouldBe(_changed);
        Directory.GetFiles(_folder.Path).Select(Path.GetFileName).ShouldBe(["settings.json"]);
    }

    [Fact]
    public async Task A_cancelled_save_throws_and_writes_nothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => Create().SaveAsync(_changed, cancelled.Token));

        Directory.GetFiles(_folder.Path).ShouldBeEmpty();
    }

    [Fact]
    public void A_path_that_is_blank_or_not_absolute_is_a_bug()
    {
        Should.Throw<ArgumentException>(() => new JsonSettingsStore(" "));
        Should.Throw<ArgumentException>(() => new JsonSettingsStore("settings.json"));
        Should.Throw<ArgumentNullException>(() => new JsonSettingsStore(null!));
    }

    [Fact]
    public async Task Missing_settings_are_a_bug()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => Create().SaveAsync(null!, Ct));
    }

    private Task WriteAsync(string content) => File.WriteAllTextAsync(SettingsPath, content, new UTF8Encoding(false), Ct);
}
