/// <summary>
/// Represents a checkbox form field.
/// </summary>
sealed class CheckBoxFormFieldElement : FormFieldElement
{
    /// <summary>Whether the checkbox is checked.</summary>
    public bool Checked { get; init; }

    /// <summary>Default checked state.</summary>
    public bool DefaultChecked { get; init; }

    /// <summary>Size of the checkbox in points (0 = auto).</summary>
    public double SizePoints { get; init; }
}