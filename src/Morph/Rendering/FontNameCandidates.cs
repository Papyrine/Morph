/// <summary>
/// Candidate font family names to try when resolving a font, in priority order.
/// </summary>
readonly record struct FontNameCandidates(string Effective, string Original, string? Stripped);
