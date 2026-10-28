/// <summary>
/// Represents a single border edge (top, right, bottom, or left).
/// </summary>
sealed record BorderEdge
{
    /// <summary>Whether this border edge should be rendered.</summary>
    public bool IsVisible { get; init; }

    /// <summary>Border width in points, straight from <c>w:sz</c>. What it means for a multi-line
    /// style depends on the family and is not the same in both: for <c>Double</c>/<c>Triple</c> it
    /// is the width of EACH line (a 3pt double stacks to 9pt), for the thin/thick pairs it is
    /// divided between them. <see cref="BorderStroke"/> owns that distinction and the Word
    /// measurements behind it.</summary>
    public double WidthPoints { get; init; } = 0.5;

    /// <summary>Border color as hex string (e.g., "000000").</summary>
    public string? ColorHex { get; init; } = "000000";

    /// <summary>Line style, the full <c>ST_Border</c> line enumeration.</summary>
    public BorderLineStyle Style { get; init; } = BorderLineStyle.Single;

    /// <summary>
    /// <c>w:space</c> in points — the gap between the text and this edge. Carried here for a RUN
    /// border (<c>w:bdr</c>), whose reserve and box <see cref="BorderStroke.RunBorderReserve"/> and
    /// <see cref="BorderStroke.RunBorderInset"/> derive from it; a paragraph edge keeps its space on
    /// <c>ParagraphProperties</c> (per side) and leaves this at zero.
    /// </summary>
    public double SpacePoints { get; init; }

    /// <summary>
    /// <c>w:shadow</c>: the edge casts a shadow. On a paragraph box Word draws it as a black band
    /// beside the right and bottom rules (<c>Fragmenter.FlushBorderRun</c>).
    /// </summary>
    internal bool Shadow { get; init; }

    /// <summary>
    /// <c>w:frame</c>. Nothing is drawn for it, but it is part of what makes two paragraphs' boxes
    /// the same box: border_style_variants sets three paragraphs alike but for <c>w:shadow</c> and
    /// <c>w:frame</c>, and Word draws three boxes, not one around the three.
    /// </summary>
    internal bool Frame { get; init; }

    /// <summary>
    /// An edge declared <c>w:val="none"</c>: it draws nothing but still holds its <c>w:space</c> open
    /// beside the text, where <c>nil</c> holds nothing. Word-probed 2026-10-28
    /// (<c>_probe_nilspace</c>, 12pt Arial paragraphs between plain references, baselines off the
    /// XPS): a <c>none</c> top or bottom edge moved the next line 6.0pt at <c>w:space="6"</c> and 19.8
    /// to 20.4pt at 20, whether its <c>w:sz</c> was 4 or 24, and a <c>nil</c> edge moved nothing at 4
    /// or 20, on one side or on all four.
    /// </summary>
    public bool HoldsSpace { get; init; }

    public static BorderEdge None => new()
    {
        IsVisible = false
    };

    public static BorderEdge Default => new()
    {
        IsVisible = true,
        WidthPoints = 0.5,
        ColorHex = "000000"
    };
}

/// <summary>
/// ECMA-376 17.18.2 <c>ST_Border</c>, line styles only — the ~160 art borders are page-border-only
/// and are modelled separately.
///
/// <para>This used to be four values (Single/Double/Dotted/Dashed) with every other OOXML style
/// folded into <c>Single</c>. That collapse was not only a rendering loss: because
/// <see cref="ParagraphProperties.SharesBorderGroupWith"/> compares border records for equality,
/// paragraphs declaring DIFFERENT styles compared equal and merged into one box — the
/// <c>border_style_variants</c> fixture's four separate three-D boxes rendered as a single
/// rectangle. Keeping the declared style distinct fixes the grouping and the stroke together.</para>
/// </summary>
enum BorderLineStyle
{
    Single,
    None,
    Thick,
    Double,
    Dotted,
    Dashed,
    DotDash,
    DotDotDash,
    Triple,
    ThinThickSmallGap,
    ThickThinSmallGap,
    ThinThickThinSmallGap,
    ThinThickMediumGap,
    ThickThinMediumGap,
    ThinThickThinMediumGap,
    ThinThickLargeGap,
    ThickThinLargeGap,
    ThinThickThinLargeGap,
    Wave,
    DoubleWave,
    DashSmallGap,
    DashDotStroked,
    ThreeDEmboss,
    ThreeDEngrave,
    Outset,
    Inset
}
