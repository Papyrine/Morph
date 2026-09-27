/// <summary>
/// One stretch of a rewritten paragraph. With <see cref="Text"/>, it is text formatted as the unit
/// <see cref="Unit"/> of the paragraph was (an index into <see cref="EditParagraph.Units"/>, or -1 for
/// the paragraph mark's own formatting), with <see cref="Format"/> set over that. Without, it is the
/// unit itself — one that is not text — kept where it stands.
/// </summary>
readonly record struct NewItem(int Unit, string? Text, RunFormat Format = default);
