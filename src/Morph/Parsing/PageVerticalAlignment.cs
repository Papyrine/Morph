/// <summary>
/// Where a section's content sits between the top and bottom margins of each page (<c>w:vAlign</c> in
/// <c>w:sectPr</c>, ECMA-376 §17.6.23) — Word's Page Setup › Layout › Vertical alignment.
/// </summary>
enum PageVerticalAlignment
{
    /// <summary>Content starts at the top margin — the default.</summary>
    Top,

    /// <summary>Content is centred between the margins (<c>center</c>).</summary>
    Center,

    /// <summary>
    /// The gaps between paragraphs (and between table rows) grow equally until the content reaches the
    /// bottom margin (<c>both</c>). The lines inside a paragraph keep their pitch.
    /// </summary>
    Justified,

    /// <summary>Content ends at the bottom margin (<c>bottom</c>).</summary>
    Bottom
}
