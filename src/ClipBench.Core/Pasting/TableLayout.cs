namespace ClipBench.Core.Pasting;

/// <summary>How selected clips fill a spreadsheet, one clip per cell.</summary>
public enum TableLayout
{
    /// <summary>The clips fill a column: one cell per line.</summary>
    Column,

    /// <summary>The clips fill a row: cells separated by tabs.</summary>
    Row,
}
