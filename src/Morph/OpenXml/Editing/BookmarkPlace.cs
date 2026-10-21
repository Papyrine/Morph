/// <summary>
/// A bookmark of the document, as <see cref="DocumentOutline"/> reads it: its name, and the place it
/// starts at, as a paragraph ordinal and a place in that paragraph's text. <see cref="Hidden"/> is one of
/// Word's own, whose name starts with an underscore.
/// </summary>
sealed record BookmarkPlace(string Name, int Paragraph, int Offset)
{
    public bool Hidden => Name.StartsWith('_');
}
