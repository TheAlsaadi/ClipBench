using ClipBench.Core.Boards;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipBench.Presentation.Boards;

/// <summary>What the popup list is showing.</summary>
public enum PopupScope
{
    /// <summary>Every clip, newest first.</summary>
    History,

    /// <summary>The pinned clips.</summary>
    Pinned,

    /// <summary>The clips on one board, in the order of the board.</summary>
    Board,
}

/// <summary>One line of the sidebar: History, Pinned or a board.</summary>
public sealed partial class SidebarEntryViewModel : ObservableObject
{
    /// <summary>Creates an entry.</summary>
    /// <param name="scope">What the entry shows.</param>
    /// <param name="board">The board, for a board entry.</param>
    /// <param name="name">What the entry says.</param>
    /// <param name="count">How many clips it holds.</param>
    public SidebarEntryViewModel(PopupScope scope, BoardId? board, string name, int count)
    {
        ArgumentNullException.ThrowIfNull(name);
        Scope = scope;
        Board = board;
        Name = name;
        Count = count;
    }

    /// <summary>What the entry shows.</summary>
    public PopupScope Scope { get; }

    /// <summary>The board, for a board entry; otherwise null.</summary>
    public BoardId? Board { get; }

    /// <summary>What the entry says. For a board it is what the user called it.</summary>
    public string Name { get; }

    /// <summary>How many clips it holds.</summary>
    [ObservableProperty]
    public partial int Count { get; set; }

    /// <summary>True for a board.</summary>
    public bool IsBoard => Scope == PopupScope.Board;

    /// <summary>The text a screen reader says.</summary>
    public string AutomationName => IsBoard
        ? $"Board {Name}, {Count} {(Count == 1 ? "clip" : "clips")}"
        : $"{Name}, {Count} {(Count == 1 ? "clip" : "clips")}";

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(AutomationName));

    /// <summary>Printing never shows a board name: it is what the user typed.</summary>
    /// <returns>The kind of entry.</returns>
    public override string ToString() => $"Sidebar entry ({Scope})";
}
