/// <summary>
/// Represents a text input form field.
/// </summary>
sealed class TextFormFieldElement : FormFieldElement
{
    /// <summary>The current text value.</summary>
    public string Value { get; init; } = "";

    /// <summary>Default/placeholder text.</summary>
    public string? DefaultText { get; init; }

    /// <summary>Maximum character length (0 = unlimited).</summary>
    public int MaxLength { get; init; }

    /// <summary>The type of text input.</summary>
    public TextFormFieldType TextType { get; init; } = TextFormFieldType.Regular;

    /// <summary>Width of the field in points (for rendering).</summary>
    public double WidthPoints { get; init; } = 100;
}