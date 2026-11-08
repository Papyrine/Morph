/// <summary>
/// One run of text to paint on a <see cref="PlacedLine"/>: its <see cref="Text"/>, the
/// <see cref="RunProperties"/> a painter resolves for font, colour and decoration, its absolute
/// <see cref="X"/> (points from the page's left) and its <see cref="Width"/> (points). A mixed-format
/// line is several runs at their own X; a uniform line is one. The width spans the run's text so a
/// painter can stroke an underline or strike and fill a highlight across it.
/// A tab-leader filler carries an empty <see cref="Text"/> and a non-<c>None</c> <see cref="Leader"/>: the
/// painter fills its span with the leader (tiled dots/hyphens, or a baseline rule for underscore).
/// <see cref="BaselineShift"/> is how far ABOVE the line baseline this run's glyphs sit, in points — a
/// superscript's raise, a subscript's (negative) drop, zero for ordinary text (<see cref="VerticalRunPosition"/>).
/// <see cref="BorderInside"/> is set on a piece of a bordered run whose border takes no room of its own:
/// the line was not grown for it, and its rules are drawn inside the line box
/// (<see cref="BorderStroke.RunBorderReserves"/>).
/// <see cref="InkOnly"/> marks a copy drawn for an effect (<see cref="TextRelief"/>): it paints, but its
/// text is not the line's text, so a text layer or an index of the line's runs skips it.
/// <see cref="GlyphX"/>, when set, is where each character of <see cref="Text"/> starts, in points from
/// <see cref="X"/> — where Word's pen draws it (<c>CanonicalParagraphMeasurer.DrawAtWordPixels</c>), its
/// kerning and tracking already in it — one entry per UTF-16 index, the characters of one cluster sharing
/// their cluster's. A painter draws each glyph there rather than advancing by its own font library's
/// widths. Null where the run is drawn by its advances.
/// </summary>
readonly record struct PlacedRun(float X, float Width, string Text, RunProperties Properties, TabLeader Leader = TabLeader.None, float BaselineShift = 0, bool BorderInside = false, bool InkOnly = false, IReadOnlyList<float>? GlyphX = null)
{
    /// <summary>
    /// How far the run's text has drawn by the end of <paramref name="prefix"/>, a prefix of
    /// <see cref="Text"/>: read off <see cref="GlyphX"/> where the run has it, else <paramref name="measure"/>'s.
    /// What places an underline under some of the run's words.
    /// </summary>
    public float PrefixWidth(string prefix, Func<string, float> measure)
    {
        if (GlyphX is not { } glyphX)
        {
            return measure(prefix);
        }

        return prefix.Length < glyphX.Count ? glyphX[prefix.Length] : Width;
    }

    /// <summary>
    /// The positions a painter draws <paramref name="text"/> at: <paramref name="glyphX"/> when it holds
    /// one entry per character of the text, else null and the painter advances by its own widths.
    /// </summary>
    public static IReadOnlyList<float>? Positions(IReadOnlyList<float>? glyphX, string text) =>
        glyphX != null && glyphX.Count == text.Length ? glyphX : null;

    /// <summary>
    /// The properties a painter makes a run's font from when it draws the run at Word's pen
    /// (<see cref="GlyphX"/>): the size Word draws its glyphs at, the em rounded to a whole pixel
    /// (<see cref="CanonicalTextMeasurer.DrawnEmPoints"/>). Only the font: where each glyph stands, the
    /// decorations and the bold simulation's stroke keep the size as written.
    /// </summary>
    public static RunProperties GlyphFont(RunProperties properties)
    {
        var size = VerticalRunPosition.RenderSizePoints(properties);
        if (size <= 0)
        {
            return properties;
        }

        var drawn = CanonicalTextMeasurer.DrawnEmPoints(size);
        if (Math.Abs(drawn - size) < 0.0001)
        {
            return properties;
        }

        return properties with
        {
            FontSizePoints = properties.FontSizePoints * drawn / size
        };
    }

    /// <summary>
    /// The end (exclusive) of the cluster that starts at <paramref name="start"/>: its first character
    /// (a surrogate pair whole) and every character after it drawn at the same place, such as a combining
    /// mark. A painter draws each cluster as one string at its position, so the mark stays on its letter.
    /// </summary>
    public static int ClusterEnd(IReadOnlyList<float> glyphX, string text, int start)
    {
        var end = start + (char.IsHighSurrogate(text[start]) && start + 1 < text.Length ? 2 : 1);
        while (end < text.Length &&
               glyphX[end] == glyphX[start])
        {
            end++;
        }

        return end;
    }
}
