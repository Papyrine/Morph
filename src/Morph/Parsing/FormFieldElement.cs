/// <summary>
/// Base class for form field elements.
/// </summary>
abstract class FormFieldElement : DocumentElement
{
    /// <summary>Name/bookmark of the form field.</summary>
    public string? Name { get; init; }

    /// <summary>Whether the field is enabled for user input.</summary>
    public bool Enabled { get; init; } = true;
}