using ClipBench.Core.History;
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

    // --- privacy and history settings (Phase 7) ---

    [Fact]
    public void The_privacy_defaults_keep_a_month_of_history_hide_secrets_and_stay_out_of_screen_captures()
    {
        var settings = AppSettings.Default;

        settings.HistoryMaxCount.ShouldBe(1000);
        settings.HistoryMaxAgeDays.ShouldBe(30);
        settings.HistoryMaxMegabytes.ShouldBe(256);
        settings.SensitiveExpirySeconds.ShouldBe(120);
        settings.MaskSensitive.ShouldBeTrue();
        settings.HideFromScreenCapture.ShouldBeTrue();
    }

    [Fact]
    public void Starting_with_Windows_is_off_and_the_first_run_is_not_done_in_a_new_installation()
    {
        AppSettings.Default.StartWithWindows.ShouldBeFalse();
        AppSettings.Default.FirstRunCompleted.ShouldBeFalse();
    }

    [Theory]
    [InlineData(nameof(AppSettings.HistoryMaxCount), 10, 10_000)]
    [InlineData(nameof(AppSettings.HistoryMaxAgeDays), 1, 365)]
    [InlineData(nameof(AppSettings.HistoryMaxMegabytes), 16, 2048)]
    [InlineData(nameof(AppSettings.SensitiveExpirySeconds), 15, 3600)]
    public void A_number_inside_its_range_is_kept_and_one_outside_it_is_moved_to_the_nearest_end(string name, int minimum, int maximum)
    {
        Normalized(name, minimum).ShouldBe(minimum);
        Normalized(name, maximum).ShouldBe(maximum);
        Normalized(name, (minimum + maximum) / 2).ShouldBe((minimum + maximum) / 2);
        Normalized(name, minimum - 1).ShouldBe(minimum);
        Normalized(name, maximum + 1).ShouldBe(maximum);
        Normalized(name, int.MinValue).ShouldBe(minimum);
        Normalized(name, int.MaxValue).ShouldBe(maximum);
        Normalized(name, 0).ShouldBe(minimum);
        Normalized(name, -5).ShouldBe(minimum);
    }

    [Fact]
    public void The_ranges_are_published_so_the_settings_window_can_use_the_same_ones()
    {
        AppSettings.HistoryMaxCountRange.ShouldBe(new SettingRange(10, 10_000));
        AppSettings.HistoryMaxAgeDaysRange.ShouldBe(new SettingRange(1, 365));
        AppSettings.HistoryMaxMegabytesRange.ShouldBe(new SettingRange(16, 2048));
        AppSettings.SensitiveExpirySecondsRange.ShouldBe(new SettingRange(15, 3600));
    }

    [Fact]
    public void Normalizing_keeps_the_switches_as_they_are()
    {
        var settings = AppSettings.Default with
        {
            MaskSensitive = false,
            HideFromScreenCapture = false,
            StartWithWindows = true,
            FirstRunCompleted = true,
        };

        settings.Normalize().ShouldBe(settings);
    }

    [Fact]
    public void A_range_knows_whether_it_holds_a_number_and_moves_one_into_itself()
    {
        var range = new SettingRange(5, 9);

        range.Contains(5).ShouldBeTrue();
        range.Contains(9).ShouldBeTrue();
        range.Contains(4).ShouldBeFalse();
        range.Contains(10).ShouldBeFalse();
        range.Clamp(2).ShouldBe(5);
        range.Clamp(12).ShouldBe(9);
        range.Clamp(7).ShouldBe(7);
    }

    [Fact]
    public void A_range_that_ends_before_it_starts_is_a_bug()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new SettingRange(9, 5));
    }

    [Fact]
    public void The_history_limits_are_the_chosen_count_age_and_size()
    {
        var options = (AppSettings.Default with { HistoryMaxCount = 250, HistoryMaxAgeDays = 7, HistoryMaxMegabytes = 64 }).ToHistoryOptions();

        options.MaxCount.ShouldBe(250);
        options.MaxAge.ShouldBe(TimeSpan.FromDays(7));
        options.MaxTotalBytes.ShouldBe(64L * 1024 * 1024);
    }

    [Fact]
    public void The_default_history_limits_are_the_ones_the_history_starts_with()
    {
        AppSettings.Default.ToHistoryOptions().ShouldBe(new HistoryOptions());
    }

    [Fact]
    public void Settings_that_were_never_normalized_still_give_usable_history_limits()
    {
        var options = (AppSettings.Default with { HistoryMaxCount = -1, HistoryMaxAgeDays = 0, HistoryMaxMegabytes = int.MaxValue }).ToHistoryOptions();

        options.MaxCount.ShouldBe(10);
        options.MaxAge.ShouldBe(TimeSpan.FromDays(1));
        options.MaxTotalBytes.ShouldBe(2048L * 1024 * 1024);
    }

    private static int Normalized(string name, int value)
    {
        var settings = name switch
        {
            nameof(AppSettings.HistoryMaxCount) => AppSettings.Default with { HistoryMaxCount = value },
            nameof(AppSettings.HistoryMaxAgeDays) => AppSettings.Default with { HistoryMaxAgeDays = value },
            nameof(AppSettings.HistoryMaxMegabytes) => AppSettings.Default with { HistoryMaxMegabytes = value },
            nameof(AppSettings.SensitiveExpirySeconds) => AppSettings.Default with { SensitiveExpirySeconds = value },
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };

        var normalized = settings.Normalize();
        return name switch
        {
            nameof(AppSettings.HistoryMaxCount) => normalized.HistoryMaxCount,
            nameof(AppSettings.HistoryMaxAgeDays) => normalized.HistoryMaxAgeDays,
            nameof(AppSettings.HistoryMaxMegabytes) => normalized.HistoryMaxMegabytes,
            _ => normalized.SensitiveExpirySeconds,
        };
    }

    [Fact]
    public void A_missing_custom_separator_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => AppSettings.Default with { CustomSeparator = null! });
    }
}
