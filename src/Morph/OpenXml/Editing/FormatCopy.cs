using OoxmlParagraphProperties = DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties;
using OoxmlRunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;

/// <summary>
/// What Format Painter picked up (<see cref="DocumentEditor.CopyFormat"/>): a run's character
/// formatting and its paragraph's, as copies of their properties. Kept as elements rather than
/// markup: read back from a string, the SDK turns w:ind's w:left into w:start.
/// </summary>
sealed record FormatCopy(OoxmlRunProperties Run, OoxmlParagraphProperties Paragraph);
