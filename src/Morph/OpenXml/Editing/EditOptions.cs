/// <summary>
/// Who is editing and when, and whether the edit is recorded as a tracked change. The author and the
/// date are written only when it is.
/// </summary>
sealed record EditOptions(string Author, DateTimeOffset Date, bool Track);
