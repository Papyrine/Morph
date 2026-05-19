/// <summary>
/// Represents a drop-down list form field.
/// </summary>
sealed class DropDownFormFieldElement : FormFieldElement
{
    /// <summary>Available options in the drop-down.</summary>
    public required IReadOnlyList<string> Items { get; init; }

    /// <summary>Index of the currently selected item (0-based).</summary>
    public int SelectedIndex { get; init; }

    /// <summary>Width of the field in points (for rendering).</summary>
    public double WidthPoints { get; init; } = 100;
}