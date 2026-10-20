/// <summary>
/// What follows a list marker (<c>w:lvl/w:suff</c>), which decides where the first line's text starts.
/// Word-probed (<c>_probe_list_suffix</c>, 2026-10-21): a tab runs to the next stop past the marker, a
/// space sets the text one space after it, nothing sets the text against it.
/// </summary>
enum ListMarkerSuffix
{
    Tab,
    Space,
    Nothing
}
