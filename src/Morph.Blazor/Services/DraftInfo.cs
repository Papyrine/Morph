/// <summary>
/// The editor's draft in the browser's storage (morph-draft.js): the document's name, and when it was
/// written, in milliseconds since 1970.
/// </summary>
sealed record DraftInfo(string Name, double SavedAt);
