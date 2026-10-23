/// <summary>
/// A floating picture where a page draws it, in points from the page's top left, and the place of its
/// drawing in the file: a paragraph's ordinal and a place in it, as DocumentEditor names pictures.
/// </summary>
sealed record PagePicture(float X, float Y, float Width, float Height, int Paragraph, int Offset);
