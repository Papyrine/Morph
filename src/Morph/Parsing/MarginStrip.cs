/// <summary>
/// A float positioned in one of the page's margin strips (<c>wp:positionV/@relativeFrom</c> <c>topMargin</c>
/// or <c>bottomMargin</c>). Word measures the top strip from the page's top edge down to where the body
/// starts, and the bottom strip from where the footer starts down to the page's bottom edge: the margin, or
/// further in when the header or footer reaches past it. So where the float stands is only known once the
/// page's bands are laid out, and the strip is resolved there (<c>Fragmenter.ResolveBandImages</c>), from the
/// float's offset (its <c>VerticalPositionPoints</c>, from the strip's top) and its alignment in the strip.
///
/// <para>Word-probed (<c>_probe_bandmargin</c>, 2026-11-19: Letter, 72pt margins, bands 36pt from the edge
/// on exact 20pt lines, 40pt rectangles): centred in the top strip, a one-line header's rectangle stood at
/// 16pt (the strip is the 72pt margin) and a six-line header's at 58pt (the strip ends at the header's
/// foot, 156pt); bottom-aligned it ended at 156pt; offset 10pt it stood at 10pt. Centred in the bottom
/// strip, a one-line footer's stood at 736pt (the 720-792pt margin) and a six-line footer's at 694pt (the
/// strip starts at the footer's top, 636pt); top-aligned it stood at 636pt; offset 10pt, at 646pt.</para>
///
/// <para>Only a header or footer float reads it. A body float anchored to a strip still stands where the
/// general margin anchor puts it, as it did before the strips were modelled.</para>
/// </summary>
/// <param name="Bottom">The bottom strip, rather than the top.</param>
/// <param name="Alignment">How it stands in the strip (<c>wp:align</c>); an offset float is top-aligned.</param>
/// <param name="AnchorHeightPoints">The height of the drawing the anchor positions — a group's, for each of
/// its children — which an alignment centres or bottoms in the strip.</param>
readonly record struct MarginStrip(bool Bottom, StripAlignment Alignment, double AnchorHeightPoints);

/// <summary>How a float stands in its margin strip (<see cref="MarginStrip"/>).</summary>
enum StripAlignment
{
    Top,
    Center,
    Bottom
}
