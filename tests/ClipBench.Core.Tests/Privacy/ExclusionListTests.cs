using ClipBench.Core.Privacy;
using ClipBench.Core.Results;

namespace ClipBench.Core.Tests.Privacy;

public class ExclusionListTests
{
    // --- the default list ---

    [Theory]
    [InlineData("KeePass.exe")]
    [InlineData("KeePassXC.exe")]
    [InlineData("1Password.exe")]
    [InlineData("Bitwarden.exe")]
    [InlineData("Dashlane.exe")]
    [InlineData("NordPass.exe")]
    [InlineData("Enpass.exe")]
    [InlineData("RoboForm.exe")]
    [InlineData("Keeper.exe")]
    [InlineData("Proton Pass.exe")]
    [InlineData("CredentialUIBroker.exe")]
    public void The_default_list_has_the_password_managers_and_the_Windows_credential_prompt(string executable)
    {
        ExclusionList.Default.Contains(executable).ShouldBeTrue();
    }

    [Fact]
    public void The_default_list_does_not_exclude_an_ordinary_program()
    {
        ExclusionList.Default.Contains("notepad.exe").ShouldBeFalse();
        ExclusionList.Default.Contains("chrome.exe").ShouldBeFalse();
    }

    [Fact]
    public void The_default_list_is_made_of_valid_unique_names()
    {
        var names = ExclusionList.Default.Names;

        names.ShouldNotBeEmpty();
        names.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(names.Length);
        ExclusionList.FromNames(names).ShouldBe(ExclusionList.Default);
    }

    [Fact]
    public void An_empty_list_excludes_nothing()
    {
        ExclusionList.Empty.Count.ShouldBe(0);
        ExclusionList.Empty.Contains("keepass.exe").ShouldBeFalse();
    }

    // --- matching ---

    [Theory]
    [InlineData("keepass.exe")]
    [InlineData("KEEPASS.EXE")]
    [InlineData("KeePass")]
    [InlineData(@"C:\Program Files\KeePass Password Safe 2\KeePass.exe")]
    [InlineData("C:/tools/keepass.exe")]
    [InlineData("  KeePass.exe  ")]
    public void A_program_matches_by_executable_name_whatever_the_case_folder_or_extension(string process)
    {
        var list = ExclusionList.Empty.Add("KeePass").Value;

        list.Contains(process).ShouldBeTrue();
    }

    [Theory]
    [InlineData("keepass2.exe")]
    [InlineData("notkeepass.exe")]
    [InlineData("keepass.exe.bak")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_program_with_another_name_or_none_does_not_match(string? process)
    {
        var list = ExclusionList.Empty.Add("KeePass").Value;

        list.Contains(process).ShouldBeFalse();
    }

    // --- adding ---

    [Theory]
    [InlineData("notepad.exe", "notepad")]
    [InlineData("Notepad", "Notepad")]
    [InlineData(@"C:\Windows\System32\Notepad.EXE", "Notepad")]
    [InlineData("  my tool  ", "my tool")]
    public void Adding_keeps_the_name_without_its_folder_and_extension(string typed, string stored)
    {
        var result = ExclusionList.Empty.Add(typed);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Names.ShouldBe([stored]);
    }

    [Fact]
    public void Adding_puts_the_name_at_the_end_and_leaves_the_original_list_alone()
    {
        var original = ExclusionList.Empty.Add("a").Value;

        var longer = original.Add("b").Value;

        original.Names.ShouldBe(["a"]);
        longer.Names.ShouldBe(["a", "b"]);
    }

    [Fact]
    public void Adding_a_program_that_is_already_there_changes_nothing_whatever_the_case()
    {
        var list = ExclusionList.Empty.Add("KeePass").Value;

        var again = list.Add("keepass.EXE");

        again.IsSuccess.ShouldBeTrue();
        again.Value.ShouldBe(list);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".exe")]
    [InlineData(@"C:\folder\")]
    [InlineData("a*b")]
    [InlineData("what?")]
    [InlineData("a|b")]
    [InlineData("two\nlines")]
    [InlineData("tab\there")]
    public void Adding_a_name_that_cannot_be_an_executable_is_refused(string typed)
    {
        var result = ExclusionList.Empty.Add(typed);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.InvalidProgramName);
        result.Error.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Adding_a_name_that_is_too_long_is_refused_and_one_at_the_limit_is_kept()
    {
        ExclusionList.Empty.Add(new string('a', ExclusionList.MaxNameLength)).IsSuccess.ShouldBeTrue();

        var result = ExclusionList.Empty.Add(new string('a', ExclusionList.MaxNameLength + 1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.InvalidProgramName);
    }

    [Fact]
    public void Adding_to_a_full_list_is_refused_but_a_program_already_in_it_is_fine()
    {
        var full = ExclusionList.FromNames(Enumerable.Range(0, ExclusionList.MaxEntries).Select(i => $"program{i}"));
        full.Count.ShouldBe(ExclusionList.MaxEntries);

        var refused = full.Add("one more");

        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe(ErrorCode.TooManyExcludedPrograms);
        full.Add("PROGRAM7").IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Adding_a_missing_name_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => ExclusionList.Empty.Add(null!));
    }

    // --- removing ---

    [Fact]
    public void Removing_takes_the_program_out_whatever_the_case_or_extension()
    {
        var list = ExclusionList.Empty.Add("KeePass").Value.Add("Bitwarden").Value;

        var shorter = list.Remove("KEEPASS.exe");

        shorter.Names.ShouldBe(["Bitwarden"]);
        list.Count.ShouldBe(2);
    }

    [Fact]
    public void Removing_a_program_that_is_not_there_changes_nothing()
    {
        var list = ExclusionList.Empty.Add("KeePass").Value;

        list.Remove("notepad").ShouldBe(list);
        list.Remove("").ShouldBe(list);
    }

    [Fact]
    public void Removing_a_missing_name_is_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => ExclusionList.Empty.Remove(null!));
    }

    // --- building from saved names ---

    [Fact]
    public void Saved_names_are_cleaned_up_duplicates_and_unusable_ones_dropped_and_the_first_spelling_kept()
    {
        var list = ExclusionList.FromNames(["KeePass.exe", "keepass", "", null, "a*b", @"C:\x\Tool.exe", "  ", "TOOL"]);

        list.Names.ShouldBe(["KeePass", "Tool"]);
    }

    [Fact]
    public void Saved_names_beyond_the_limit_are_dropped()
    {
        var list = ExclusionList.FromNames(Enumerable.Range(0, ExclusionList.MaxEntries + 50).Select(i => $"program{i}"));

        list.Count.ShouldBe(ExclusionList.MaxEntries);
        list.Names[^1].ShouldBe($"program{ExclusionList.MaxEntries - 1}");
    }

    [Fact]
    public void Missing_saved_names_are_a_bug()
    {
        Should.Throw<ArgumentNullException>(() => ExclusionList.FromNames(null!));
    }

    // --- equality ---

    [Fact]
    public void Lists_with_the_same_names_in_the_same_order_are_equal()
    {
        var one = ExclusionList.Empty.Add("a").Value.Add("b").Value;
        var two = ExclusionList.FromNames(["a", "b"]);
        var other = ExclusionList.FromNames(["b", "a"]);

        one.ShouldBe(two);
        (one == two).ShouldBeTrue();
        one.GetHashCode().ShouldBe(two.GetHashCode());
        one.ShouldNotBe(other);
        (one != other).ShouldBeTrue();
        one.Equals(null).ShouldBeFalse();
        one.Equals((object)"a").ShouldBeFalse();
    }
}
