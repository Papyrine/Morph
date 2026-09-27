/// <summary>What a stretch of a paragraph is to someone editing it.</summary>
enum EditUnitKind
{
    /// <summary>Text that can be edited: typed text, tabs, line breaks.</summary>
    Text,

    /// <summary>
    /// The text of a tracked deletion, or of the place a move came from. It is shown, as Word shows
    /// it, and is settled in the review pane rather than edited.
    /// </summary>
    Deleted,

    /// <summary>A field, from where it begins to where it ends. Its result is computed, not typed.</summary>
    Field,

    /// <summary>A footnote or endnote reference.</summary>
    Note,

    /// <summary>A picture, a shape or an embedded object.</summary>
    Drawing,

    /// <summary>A page or column break.</summary>
    Break
}
