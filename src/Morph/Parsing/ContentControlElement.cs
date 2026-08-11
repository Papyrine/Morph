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

    bool cellParagraphBuilt;

    /// <summary>
    /// The synthetic paragraph wrapping this control's runs (falling back to plain-text
    /// content) for table-cell measurement and rendering. A single shared instance: the render
    /// backends' layout caches key on <see cref="ParagraphElement"/> identity, so if each
    /// pipeline stage (autofit, row height, vertical-align measure, draw) wrapped the runs in
    /// its own fresh paragraph, every stage would re-lay the same content out from cold.
    /// Null when the control has neither runs nor text.
    /// </summary>
    public ParagraphElement? CellParagraph
    {
        get
        {
            if (!cellParagraphBuilt)
            {
                cellParagraphBuilt = true;
                if (Runs is {Count: > 0})
                {
                    field = new()
                    {
                        Runs = Runs,
                        Properties = new()
                    };
                }
                else if (!string.IsNullOrEmpty(Content))
                {
                    field = new()
                    {
                        Runs =
                        [
                            new()
                            {
                                Text = Content,
                                Properties = new()
                            }
                        ],
                        Properties = new()
                    };
                }
                else if (ControlType == ContentControlType.Date && DateValue.HasValue)
                {
                    // A date control whose display text was not captured as runs still shows its
                    // value, formatted per the control's declared format (invariant culture, so
                    // page output is deterministic across machines).
                    field = new()
                    {
                        Runs =
                        [
                            new()
                            {
                                Text = DateControlText.Resolve(this),
                                Properties = new()
                            }
                        ],
                        Properties = new()
                    };
                }
                else if (!string.IsNullOrEmpty(PlaceholderText))
                {
                    // An empty control shows its placeholder, grayed the way Word does.
                    field = new()
                    {
                        Runs =
                        [
                            new()
                            {
                                Text = PlaceholderText,
                                Properties = new()
                                {
                                    ColorHex = "808080"
                                }
                            }
                        ],
                        Properties = new()
                    };
                }
            }

            return field;
        }
    }

    /// <summary>For checkbox controls, whether it's checked.</summary>
    public bool? Checked { get; init; }

    /// <summary>For drop-down/combo controls, the list items.</summary>
    public IReadOnlyList<string>? ListItems { get; init; }

    /// <summary>For date controls, the selected date (from <c>w:date/@w:fullDate</c>).</summary>
    public DateTime? DateValue { get; init; }

    /// <summary>
    /// For date controls, the declared display format (<c>w:date/w:dateFormat/@w:val</c>). Used
    /// only when the control has no run text to render verbatim; formatting always uses the
    /// invariant culture so page output is deterministic across machines.
    /// </summary>
    public string? DateFormat { get; init; }

    /// <summary>Width hint in points (for rendering).</summary>
    public double WidthPoints { get; init; } = 100;
}
