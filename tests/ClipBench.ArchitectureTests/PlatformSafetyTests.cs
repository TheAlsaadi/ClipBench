using System.Reflection;
using System.Text.RegularExpressions;
using ArchUnitNET.Fluent;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace ClipBench.ArchitectureTests;

/// <summary>
/// Security baseline for the Windows layer (the project's security rules, ADR 0002 and ADR 0010): no key hooks, nothing that
/// reads the keyboard beyond the modifier keys, no way to start programs or run a clipboard path, no privilege
/// changes, no reaching into other processes, and no more rights over another process than "ask what program it
/// is". The generator's list of Win32 functions is checked, the built assembly is checked, and the source is checked.
/// </summary>
public partial class PlatformSafetyTests
{
    // Each group is a kind of function ClipBench must never call, and why. A name matches when it starts with a listed one.
    private static readonly (string Reason, string Pattern)[] _forbidden =
    [
        ("A keyboard or mouse hook looks like a keylogger. Shortcuts use RegisterHotKey (ADR 0002).", @"^(SetWindowsHookEx|UnhookWindowsHookEx|CallNextHookEx|WH_)"),
        ("Raw input and keyboard-state reads can record keystrokes. Only the modifier keys are ever read, with GetAsyncKeyState.", @"^(RegisterRawInputDevices|GetRawInput|GetKeyboardState|GetKeyState|ToUnicode|GetKeyNameText)"),
        ("Starting programs could run a path that came from the clipboard.", @"^(ShellExecute|CreateProcess|WinExec|CreateProcessAsUser|CreateProcessWithLogon|CreateProcessWithToken)"),
        ("ClipBench never changes its privileges or acts as another user (it runs as invoker).", @"^(AdjustTokenPrivileges|LookupPrivilegeValue|ImpersonateLoggedOnUser|DuplicateToken|LogonUser|SetThreadToken)"),
        ("ClipBench never reads or writes the memory of another process.", @"^(ReadProcessMemory|WriteProcessMemory|VirtualAllocEx|VirtualProtectEx|CreateRemoteThread|NtCreateThread|QueueUserAPC|SetWindowLongPtr)"),
    ];

    private static readonly string[] _modifierKeys = ["VK_CONTROL", "VK_MENU", "VK_SHIFT", "VK_LWIN", "VK_RWIN"];

    // The only names that may follow PROCESS_ in the source: the one access right ClipBench asks for, and the type and format names around it.
    private static readonly string[] _allowedProcessNames =
    [
        "PROCESS_QUERY_LIMITED_INFORMATION", "PROCESS_ACCESS_RIGHTS", "PROCESS_NAME_FORMAT", "PROCESS_NAME_WIN32",
    ];

    [GeneratedRegex(@"PROCESS_[A-Z0-9_]+")]
    private static partial Regex ProcessNames();

    [GeneratedRegex(@"//[^\r\n]*")]
    private static partial Regex LineComments();

    [GeneratedRegex(@"IsDown\(VIRTUAL_KEY\.(?<key>\w+)\)")]
    private static partial Regex KeysRead();

    private static IReadOnlyList<string> RequestedApis() =>
        [.. File.ReadAllLines(RepositoryRoot.Combine("src", "ClipBench.Platform.Windows", "NativeMethods.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))];

    private static IEnumerable<(string Path, string Text)> SourceFiles() =>
        Directory.EnumerateFiles(RepositoryRoot.Combine("src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Path.GetRelativePath(RepositoryRoot.Path, path).Replace('\\', '/'), File.ReadAllText(path)));

    // --- the list the generator is given ---

    [Fact]
    public void The_list_of_Win32_functions_is_not_empty_and_has_no_duplicates()
    {
        var apis = RequestedApis();

        apis.ShouldNotBeEmpty();
        apis.Distinct(StringComparer.Ordinal).Count().ShouldBe(apis.Count, "a name listed twice is a merge mistake");
    }

    [Fact]
    public void The_list_of_Win32_functions_names_no_hook_keyboard_reader_launcher_privilege_change_or_process_memory_access()
    {
        var offenders = from api in RequestedApis()
                        from rule in _forbidden
                        where Regex.IsMatch(api, rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))
                        select $"{api}: {rule.Reason}";

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Every_function_in_the_list_is_used_by_the_code()
    {
        // A name left in the list after its last use still gets generated code. Keeping the list to what is called keeps the review small.
        var code = string.Join('\n', SourceFiles().Select(file => LineComments().Replace(file.Text, string.Empty)));
        var unused = RequestedApis().Where(api => !Regex.IsMatch(code, $@"\b{Regex.Escape(api)}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))).ToList();

        unused.ShouldBeEmpty("remove these from NativeMethods.txt, or use them");
    }

    // --- the built assembly ---

    [Fact]
    public void The_built_Windows_layer_declares_none_of_the_forbidden_functions_or_constants()
    {
        // Checks what was really generated, not just what the list says: a generator option or a hand-written declaration would show up here.
        var assembly = Assembly.LoadFrom(SourceAssemblies.PathOf("ClipBench.Platform.Windows"));
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var names = assembly.GetTypes()
            .SelectMany(type => type.GetMethods(all).Select(method => method.Name).Concat(type.GetFields(all).Select(field => field.Name)))
            .Distinct()
            .ToList();

        names.ShouldContain("RegisterHotKey", "the check must see the generated declarations, or it proves nothing");
        var offenders = from name in names
                        from rule in _forbidden
                        where Regex.IsMatch(name, rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))
                        select $"{name}: {rule.Reason}";
        offenders.ShouldBeEmpty();
    }

    // --- the source ---

    [Fact]
    public void No_source_file_names_a_hook_a_launcher_or_process_creation()
    {
        string[] words = ["SetWindowsHookEx", "WH_KEYBOARD_LL", "WH_MOUSE_LL", "ShellExecute", "CreateProcess", "WinExec", "Process.Start", "ProcessStartInfo", "AdjustTokenPrivileges"];

        var offenders = from file in SourceFiles()
                        from word in words
                        where file.Text.Contains(word, StringComparison.Ordinal)
                        select $"{file.Path}: {word}";

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Other_processes_are_only_ever_asked_for_the_right_to_read_limited_information()
    {
        var offenders = from file in SourceFiles()
                        from match in ProcessNames().Matches(file.Text).Select(match => match.Value)
                        where !_allowedProcessNames.Contains(match, StringComparer.Ordinal)
                        select $"{file.Path}: {match}";

        offenders.ShouldBeEmpty("a process is opened with PROCESS_QUERY_LIMITED_INFORMATION and nothing more");
    }

    [Fact]
    public void The_keyboard_is_polled_for_modifier_keys_only_and_only_in_one_place()
    {
        var users = SourceFiles().Where(file => file.Text.Contains("GetAsyncKeyState", StringComparison.Ordinal)).Select(file => file.Path).ToList();

        users.ShouldBe(["src/ClipBench.Platform.Windows/Pasting/Win32KeyboardApi.cs"]);
        var keys = KeysRead().Matches(SourceFiles().Single(file => file.Path == users[0]).Text).Select(match => match.Groups["key"].Value).ToList();
        keys.ShouldNotBeEmpty();
        keys.ShouldBeSubsetOf(_modifierKeys, "only Ctrl, Alt, Shift and the Windows keys are ever read");
    }

    // --- what the built assemblies depend on ---

    public static TheoryData<string> AssembliesThatNeverStartPrograms => new("ClipBench.Core", "ClipBench.Storage", "ClipBench.Platform.Windows", "ClipBench.Presentation");

    [Theory]
    [MemberData(nameof(AssembliesThatNeverStartPrograms))]
    public void These_assemblies_do_not_depend_on_anything_that_starts_a_program(string assemblyName)
    {
        // The app project is left out until it has code of its own to judge; Phase 4 adds its rule.
        var assembly = SourceArchitecture.Value.Assemblies.Single(candidate => candidate.Name == assemblyName);
        var rule = Types().That().ResideInAssembly(assembly)
            .Should().NotDependOnAnyTypesThat().HaveFullNameMatching(@"^(System\.Diagnostics\.(Process|ProcessStartInfo)|Windows\.System\.Launcher)$")
            .WithoutRequiringPositiveResults();

        var violations = rule.Evaluate(SourceArchitecture.Value).Where(result => !result.Passed).Select(result => result.Description).ToList();

        violations.ShouldBeEmpty();
    }

    // --- unsafe code ---

    [Fact]
    public void Unsafe_code_is_allowed_in_the_Windows_layer_and_nowhere_else_in_the_source()
    {
        var allowed = ProjectFile.LoadAll("src").Where(project => project.AllowsUnsafeCode).Select(project => project.Name).ToList();

        allowed.ShouldBe(["ClipBench.Platform.Windows"]);
    }
}
