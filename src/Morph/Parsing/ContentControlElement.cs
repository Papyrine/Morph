/// <summary>
/// Represents a content control (structured document tag).
/// </summary>
sealed class ContentControlElement : DocumentElement
{
    /// <summary>The type of content control.</summary>
    public ContentControlType ControlType { get; init; } = ContentControlType.RichText;

    /// <summary>Tag name for the control.</summary>
    public string? Tag { get; init; }

    /// <summary>Title/label for the control.</summary>
    public string? Title { get; init; }

    /// <summary>Placeholder text when empty.</summary>
    public string? PlaceholderText { get; init; }

    /// <summary>Current text content (plain text for backward compatibility).</summary>
    public string Content { get; init; } = "";

    /// <summary>Styled runs within the content control (preserves formatting).</summary>
    public IReadOnlyList<Run>? Runs { get; init; }

    /// <summary>For checkbox controls, whether it's checked.</summary>
    public bool? Checked { get; init; }

    /// <summary>For drop-down/combo controls, the list items.</summary>
    public IReadOnlyList<string>? ListItems { get; init; }

    /// <summary>For date controls, the selected date.</summary>
    public DateTime? DateValue { get; init; }

    /// <summary>Width hint in points (for rendering).</summary>
    public double WidthPoints { get; init; } = 100;
}