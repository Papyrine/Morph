/// <summary>
/// Candidate font family names to try when resolving a font, in priority order: the family as the
/// document names it, then that name with its weight and style words stripped.
/// </summary>
readonly record struct FontNameCandidates(string Original, string? Stripped);
