namespace Morph;

/// <summary>
/// How much of the paper page each rendered image covers. Pass to
/// <see cref="ImageExportOptions.Crop"/>.
///
/// <para>Cropping happens after the page is painted, so it changes only the emitted rectangle:
/// layout, pagination and line breaking are identical to a full-page render, and the pixels that
/// survive are the same ones, at the same <see cref="ImageExportOptions.Dpi"/>. Nothing is
/// rescaled to fill the smaller image.</para>
/// </summary>
public enum PageCrop
{
    /// <summary>The whole sheet, margins included. The default.</summary>
    FullPage,

    /// <summary>
    /// The content box only — all four margins are dropped. Headers and footers go with them,
    /// since Word draws both inside the margin, as does any page-anchored art that reaches past
    /// the content box.
    /// </summary>
    ContentBox,

    /// <summary>
    /// The content box with the top and bottom edges pushed back out to the header and footer
    /// bands, so both survive. The left and right margins are dropped as for
    /// <see cref="ContentBox"/>, which still cuts the negative-indent tables that headers and
    /// footers use to draw a full-bleed banner.
    /// </summary>
    ContentBoxWithHeaderFooter
}
