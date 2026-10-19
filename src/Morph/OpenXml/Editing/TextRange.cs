/// <summary>
/// A stretch of one paragraph: places <see cref="From"/> up to <see cref="To"/> of the paragraph with
/// ordinal <see cref="Paragraph"/>, counted as <see cref="EditParagraph.Length"/> counts them.
/// </summary>
readonly record struct TextRange(int Paragraph, int From, int To);
