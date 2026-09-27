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
    public const string Review = "M21 15a2 2 0 0 1-2 2H8l-5 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z";
    public const string Undo = "M9 14L4 9l5-5 M4 9h11a5 5 0 0 1 0 10h-3";
    public const string Redo = "M15 14l5-5-5-5 M20 9H9a5 5 0 0 0 0 10h3";
    public const string Accept = "M20 6L9 17l-5-5";
    public const string Edit = "M12 20h9 M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z";
    public const string Bold = "M7 5h6a3.5 3.5 0 0 1 0 7H7z M7 12h7a3.5 3.5 0 0 1 0 7H7z";
    public const string Italic = "M19 5h-8 M13 19H5 M15 5L9 19";
    public const string Underline = "M7 4v6a5 5 0 0 0 10 0V4 M5 20h14";
    public const string Strike = "M16.5 7A4 4 0 0 0 13 5h-2a3.5 3.5 0 0 0-2.6 5.8 M4 12h16 M7.5 17a4 4 0 0 0 3.5 2h2a3.5 3.5 0 0 0 3-5.3";
    public const string AlignLeft = "M3 6h18 M3 10h12 M3 14h18 M3 18h12";
    public const string AlignCenter = "M3 6h18 M6 10h12 M3 14h18 M6 18h12";
    public const string AlignRight = "M3 6h18 M9 10h12 M3 14h18 M9 18h12";
    public const string AlignJustify = "M3 6h18 M3 10h18 M3 14h18 M3 18h18";
    public const string Track = "M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z M14 3v6h6 M9 13h6 M12 10v6 M9 18h6";
}
