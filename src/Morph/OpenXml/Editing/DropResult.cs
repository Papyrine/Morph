/// <summary>
/// A document after text was dragged and dropped (<see cref="DocumentEditor.Drop"/>), and where what was
/// dropped now stands: from <see cref="StartOffset"/> in paragraph <see cref="StartParagraph"/> to
/// <see cref="EndOffset"/> in <see cref="EndParagraph"/>, counted as <see cref="EditParagraph.Length"/>
/// counts — what Word leaves selected. A drop that changes nothing hands back the very array it was
/// given, with the caret at the drop.
/// </summary>
sealed record DropResult(byte[] Document, int StartParagraph, int StartOffset, int EndParagraph, int EndOffset);
