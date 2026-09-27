/// <summary>
/// Character formatting an edit sets. A null member leaves that property as it is; a value sets it
/// outright, over whatever the run's style says.
/// </summary>
readonly record struct RunFormat(bool? Bold = null, bool? Italic = null, bool? Underline = null, bool? Strike = null)
{
    public bool IsEmpty =>
        Bold == null &&
        Italic == null &&
        Underline == null &&
        Strike == null;
}
