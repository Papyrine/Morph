/// <summary>How a table's columns are sized, as Word's AutoFit menu offers it.</summary>
enum TableFit
{
    /// <summary>Each column as wide as what is in it.</summary>
    Contents,

    /// <summary>The table as wide as the text, its columns in proportion.</summary>
    Window,

    /// <summary>The columns at the widths they have now, whatever is in them.</summary>
    Fixed
}
