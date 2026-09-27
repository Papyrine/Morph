/// <summary>
/// An edited document, and the place in it the edit left off: <see cref="Offset"/> places into the
/// paragraph with ordinal <see cref="Paragraph"/>, counted as <see cref="EditParagraph.Length"/> counts.
/// </summary>
sealed record EditResult(byte[] Document, int Paragraph, int Offset);
