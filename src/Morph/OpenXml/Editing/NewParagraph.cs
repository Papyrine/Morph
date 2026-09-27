/// <summary>
/// What a paragraph is to become: its content in order, and its alignment if that is to change.
/// <see cref="DocumentEditor.Rewrite"/> takes one or more of these for one paragraph of the document —
/// more than one when the paragraph was split.
/// </summary>
sealed record NewParagraph(IReadOnlyList<NewItem> Items, TextAlignment? Alignment = null);
