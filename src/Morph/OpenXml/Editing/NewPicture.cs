/// <summary>
/// A picture to put in a document's text: the image file's bytes and media type, the size it is to be
/// drawn at, in points, and the words that describe it to someone who cannot see it.
/// </summary>
sealed record NewPicture(byte[] Data, string ContentType, double WidthPoints, double HeightPoints, string? Description = null);
