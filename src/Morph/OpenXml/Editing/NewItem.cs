/// <summary>
/// One stretch of a rewritten paragraph. With <see cref="Text"/>, it is text formatted as the unit
/// <see cref="Unit"/> of the paragraph was (an index into <see cref="EditParagraph.Units"/>, or -1 for
/// the paragraph mark's own formatting), with <see cref="Format"/> set over that. Without, it is the
/// unit itself — one that is not text — kept where it stands.
///
/// <para>An editor that tracks what it did says so: <see cref="Struck"/> text is the unit's own text,
/// deleted where it stands, and <see cref="Typed"/> text is new. Between them they say exactly which
/// characters changed and where, which <see cref="DocumentEditor.Rewrite"/> then takes as given rather
/// than working out.</para>
/// </summary>
readonly record struct NewItem(int Unit, string? Text, RunFormat Format = default, bool Struck = false, bool Typed = false);
