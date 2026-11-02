/// <summary>
/// A floating picture where a page draws it, in points from the page's top left, and the place of its
/// drawing in the file: a paragraph's ordinal and a place in it, as DocumentEditor names pictures.
/// Behind says it is drawn behind the text, which then takes a click over it before the picture does.
/// </summary>
sealed record PagePicture(float X, float Y, float Width, float Height, int Paragraph, int Offset, bool Behind = false);
