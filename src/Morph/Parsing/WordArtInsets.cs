/// <summary>
/// The insets of a WordArt shape's text rect from its box, in points — <c>wps:bodyPr</c>'s
/// <c>lIns</c> / <c>tIns</c> / <c>rIns</c> / <c>bIns</c>.
/// </summary>
readonly record struct WordArtInsets(double Left, double Top, double Right, double Bottom)
{
    /// <summary>DrawingML's defaults: 0.1in at the sides, 0.05in top and bottom.</summary>
    public static WordArtInsets Default { get; } = new(7.2, 3.6, 7.2, 3.6);
}
