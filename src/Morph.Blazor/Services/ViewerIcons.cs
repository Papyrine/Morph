/// <summary>
/// The viewer toolbar's icons: stroked 24×24 paths, drawn in the button's text colour so they follow the
/// host's theme. Inline rather than an icon font or sprite, so the package ships no extra asset to fetch.
/// </summary>
static class ViewerIcons
{
    public const string Sidebar = "M3 4h18v16H3z M9 4v16";
    public const string Search = "M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14z M21 21l-5.2-5.2";
    public const string Up = "M6 15l6-6 6 6";
    public const string Down = "M6 9l6 6 6-6";
    public const string Minus = "M5 12h14";
    public const string Plus = "M12 5v14 M5 12h14";
    public const string Rotate = "M20 12a8 8 0 1 1-2.34-5.66 M20 4v5h-5";
    public const string Open = "M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z";
    public const string Presentation = "M8 3H5a2 2 0 0 0-2 2v3 M21 8V5a2 2 0 0 0-2-2h-3 M3 16v3a2 2 0 0 0 2 2h3 M16 21h3a2 2 0 0 0 2-2v-3";
    public const string Print = "M6 9V3h12v6 M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2 M6 14h12v7H6z";
    public const string Download = "M12 3v12 M7 10l5 5 5-5 M5 21h14";
    public const string Close = "M6 6l12 12 M18 6L6 18";
}
