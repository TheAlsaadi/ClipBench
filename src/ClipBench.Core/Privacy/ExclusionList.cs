using System.Collections.Immutable;
using ClipBench.Core.Results;

namespace ClipBench.Core.Privacy;

/// <summary>
/// The programs whose clipboard changes are never recorded, as bare executable names such as <c>KeePass</c>. A name
/// matches whatever its case, folder or <c>.exe</c> ending. The list never changes: adding or removing gives a new
/// list. Two lists are equal when they hold the same names in the same order.
/// </summary>
public sealed class ExclusionList : IEquatable<ExclusionList>
{
    /// <summary>The most programs a list can hold.</summary>
    public const int MaxEntries = 200;

    /// <summary>The longest program name, in characters.</summary>
    public const int MaxNameLength = 100;

    private const string InvalidCharacters = "\"<>|:*?";

    private static readonly string[] _defaultNames =
    [
        "KeePass",
        "KeePassXC",
        "1Password",
        "Bitwarden",
        "Dashlane",
        "NordPass",
        "Enpass",
        "RoboForm",
        "RoboTaskBarIcon",
        "KeeperPasswordManager",
        "Proton Pass",
        "CredentialUIBroker",
    ];

    private ExclusionList(ImmutableArray<string> names) => Names = names;

    /// <summary>Nothing excluded.</summary>
    public static ExclusionList Empty { get; } = new([]);

    /// <summary>
    /// The password managers that people commonly use, and the Windows credential prompt, which handles the secrets it
    /// shows. This is where a new installation starts, and the user can change it.
    /// </summary>
    public static ExclusionList Default { get; } = FromNames(_defaultNames);

    /// <summary>The program names, in the order they were added.</summary>
    public ImmutableArray<string> Names { get; }

    /// <summary>How many programs the list holds.</summary>
    public int Count => Names.Length;

    /// <summary>Makes a list from saved names. Names that cannot be used and repeats are dropped, and so is everything beyond <see cref="MaxEntries"/>.</summary>
    /// <param name="names">The names, as saved.</param>
    /// <returns>The list.</returns>
    public static ExclusionList FromNames(IEnumerable<string?> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var kept = ImmutableArray.CreateBuilder<string>();
        foreach (var name in names)
        {
            if (kept.Count >= MaxEntries)
            {
                break;
            }

            if (Clean(name) is { } cleaned && !IndexOf(kept, cleaned, out _))
            {
                kept.Add(cleaned);
            }
        }

        return new ExclusionList(kept.ToImmutable());
    }

    /// <summary>Tells whether a program is on the list.</summary>
    /// <param name="process">The executable name or path as Windows reports it.</param>
    /// <returns>True when the program is excluded.</returns>
    public bool Contains(string? process)
    {
        var name = ProcessName.Normalize(process);
        return name.Length > 0 && IndexOf(Names, name, out _);
    }

    /// <summary>Adds a program to the end of the list.</summary>
    /// <param name="program">The executable name or path as the user typed it.</param>
    /// <returns>
    /// The longer list, this list when the program is already on it, or why the name was refused:
    /// <see cref="ErrorCode.InvalidProgramName"/> or <see cref="ErrorCode.TooManyExcludedPrograms"/>.
    /// </returns>
    public Result<ExclusionList> Add(string program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var name = ProcessName.Normalize(program);
        if (name.Length == 0 || name.Length > MaxNameLength || HasUnusableCharacter(name))
        {
            return Result.Failure<ExclusionList>(new ResultError(
                ErrorCode.InvalidProgramName,
                $"Type the name of a program, such as notepad.exe. A name has at most {MaxNameLength} characters and no line breaks or any of {InvalidCharacters}."));
        }

        if (IndexOf(Names, name, out _))
        {
            return Result.Success(this);
        }

        return Count >= MaxEntries
            ? Result.Failure<ExclusionList>(new ResultError(ErrorCode.TooManyExcludedPrograms, $"The list can hold at most {MaxEntries} programs. Remove one first."))
            : Result.Success(new ExclusionList(Names.Add(name)));
    }

    /// <summary>Takes a program off the list.</summary>
    /// <param name="program">The executable name or path.</param>
    /// <returns>The shorter list, or this list when the program is not on it.</returns>
    public ExclusionList Remove(string program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var name = ProcessName.Normalize(program);
        return name.Length > 0 && IndexOf(Names, name, out var index) ? new ExclusionList(Names.RemoveAt(index)) : this;
    }

    /// <inheritdoc/>
    public bool Equals(ExclusionList? other) => other is not null && Names.SequenceEqual(other.Names, StringComparer.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ExclusionList);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var name in Names)
        {
            hash.Add(name, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <summary>Tells whether two lists hold the same names in the same order.</summary>
    /// <param name="left">One list.</param>
    /// <param name="right">The other list.</param>
    /// <returns>True when they are equal.</returns>
    public static bool operator ==(ExclusionList? left, ExclusionList? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Tells whether two lists differ.</summary>
    /// <param name="left">One list.</param>
    /// <param name="right">The other list.</param>
    /// <returns>True when they are not equal.</returns>
    public static bool operator !=(ExclusionList? left, ExclusionList? right) => !(left == right);

    private static string? Clean(string? name)
    {
        var normalized = ProcessName.Normalize(name);
        return normalized.Length == 0 || normalized.Length > MaxNameLength || HasUnusableCharacter(normalized) ? null : normalized;
    }

    private static bool IndexOf(IReadOnlyList<string> names, string name, out int index)
    {
        for (index = 0; index < names.Count; index++)
        {
            if (string.Equals(names[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static bool HasUnusableCharacter(string name) =>
        name.Any(character => char.IsControl(character) || InvalidCharacters.Contains(character, StringComparison.Ordinal));
}
