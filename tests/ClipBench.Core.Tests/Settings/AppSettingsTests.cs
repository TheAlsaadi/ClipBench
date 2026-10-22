using ClipBench.Core.Pasting;
using ClipBench.Core.Settings;
using ClipBench.Core.Tests.Support;

namespace ClipBench.Core.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void The_defaults_join_with_a_line_break_restore_the_clipboard_and_keep_no_copies_while_collecting()
    {
        var settings = AppSettings.Default;

        settings.Separator.ShouldBe(SeparatorChoice.NewLine);
        settings.CustomSeparator.ShouldBe(string.Empty);
        settings.RestorePreviousClipboard.ShouldBeTrue();
        settings.KeepCopiesWhileCollecting.ShouldBeFalse();
    }

    [Theory]
    [InlineData(SeparatorChoice.None, "None")]
    [InlineData(SeparatorChoice.NewLine, "NewLine")]
    [InlineData(SeparatorChoice.BlankLine, "BlankLine")]
    [InlineData(SeparatorChoice.Space, "Space")]
    [InlineData(SeparatorChoice.Tab, "Tab")]
    [InlineData(SeparatorChoice.Comma, "Comma")]
    [InlineData(SeparatorChoice.CommaSpace, "CommaSpace")]
    [InlineData(SeparatorChoice.Semicolon, "Semicolon")]
    public void Each_preset_choice_resolves_to_its_separator(SeparatorChoice choice, string preset)
    {
        var settings = AppSettings.Default with { Separator = choice };

        settings.ResolveSeparator().ShouldBe(SeparatorNames.Get(preset));
    }

    [Fact]
    public void A_custom_choice_resolves_the_typed_escape_sequences()
    {
        var settings = AppSettings.Default with { Separator = SeparatorChoice.Custom, CustomSeparator = @" |\t" };

        settings.ResolveSeparator().Text.ShouldBe(" |\t");
    }

    [Fact]
    public void A_custom_choice_that_cannot_be_read_resolves_to_a_line_break()
    {
        var settings = AppSettings.Default with { Separator = SeparatorChoice.Custom, CustomSeparator = @"\q" };

        settings.ResolveSeparator().ShouldBe(Separator.NewLine);
    }

    [Fact]
    public void An_unknown_choice_resolves_to_a_line_break()
    {
        var settings = AppSettings.Default with { Separator = (SeparatorChoice)99 };

        settings.ResolveSeparator().ShouldBe(Separator.NewLine);
    }

    [Fact]
    public void Normalizing_valid_settings_changes_nothing()
    {
        var settings = AppSettings.Default with
        {
            Separator = SeparatorChoice.Custom,
            CustomSeparator = @"\r\n--\r\n",
            RestorePreviousClipboard = false,
            KeepCopiesWhileCollecting = true,
        };

        settings.Normalize().ShouldBe(settings);
    }

    [Fact]
    public void Normalizing_an_unknown_choice_makes_it_a_line_break()
    {
        var settings = AppSettings.Default with { Separator = (SeparatorChoice)99 };

        settings.Normalize().Separator.ShouldBe(SeparatorChoice.NewLine);
    }

    [Fact]
    public void Normalizing_an_unreadable_custom_separator_in_use_falls_back_to_a_line_break()
    {
        var settings = AppSettings.Default with { Separator = SeparatorChoice.Custom, CustomSeparator = @"\q" };

        var normalized = settings.Normalize();

        normalized.Separator.ShouldBe(SeparatorChoice.NewLine);
        normalized.CustomSeparator.ShouldBe(string.Empty);
    }

    [Fact]
    public void Normalizing_an_unreadable_custom_separator_not_in_use_clears_it_and_keeps_the_choice()
    {
        var settings = AppSettings.Default with { Separator = SeparatorChoice.Comma, CustomSeparator = "trailing \\" };

        var normalized = settings.Normalize();

        normalized.Separator.ShouldBe(SeparatorChoice.Comma);
        normalized.CustomSeparator.ShouldBe(string.Empty);
    }

    [Fact]
    public void Normalizing_clears_a_typed_custom_separator_longer_than_any_valid_one_could_be_typed()
    {
        var typed = new string('x', AppSettings.MaxTypedSeparatorLength + 1);
        var settings = AppSettings.Default with { Separator = SeparatorChoice.Custom, CustomSeparator = typed };

        var normalized = settings.Normalize();

        normalized.Separator.ShouldBe(SeparatorChoice.NewLine);
        normalized.CustomSeparator.ShouldBe(string.Empty);
    }

    [Fact]
    public void The_longest_typed_custom_separator_is_every_character_escaped()
    {
        AppSettings.MaxTypedSeparatorLength.ShouldBe(Separator.MaxCustomLength * 2);
        var typed = string.Concat(Enumerable.Repeat(@"\t", Separator.MaxCustomLength));
        var settings = AppSettings.Default with { Separator = SeparatorChoice.Custom, CustomSeparator = typed };

        settings.Normalize().ShouldBe(settings);
        settings.ResolveSeparator().Text.ShouldBe(new string('\t', Separator.MaxCustomLength));
    }

    [Fact]
    public void A_missing_custom_separator_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => AppSettings.Default with { CustomSeparator = null! });
    }
}
