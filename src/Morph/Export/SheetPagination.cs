namespace Morph;

/// <summary>
/// How an Excel workbook is divided into images. Pass to
/// <see cref="ImageExportOptions.SheetPagination"/>.
/// </summary>
public enum SheetPagination
{
    /// <summary>
    /// As the workbook prints: each sheet on its own paper size and orientation, scaled as its page
    /// setup asks, with a long sheet running onto further pages. The default.
    /// </summary>
    PrintLayout,

    /// <summary>
    /// One image for each visible sheet, in tab order, as large as what the sheet holds.
    ///
    /// <para>Nothing the sheet says about printing applies: not its paper size, orientation or
    /// margins, not its print area, print titles or page breaks, and not its scale or fit-to-page.
    /// The grid is drawn at its own size from the first used cell to the last, along with whatever
    /// is drawn on the sheet, and the image is exactly as large as that comes to, at
    /// <see cref="ImageExportOptions.Dpi"/>.</para>
    ///
    /// <para>The Nth image is always the Nth visible sheet, which is what lets a caller pair an
    /// image with the sheet it is of. To keep that so, a visible sheet with nothing on it is still
    /// an image, of the one empty cell, and so is a tab that is not a grid at all, such as a chart
    /// sheet. A hidden sheet has none.</para>
    ///
    /// <para>A sheet has no upper size, so neither has its image, and a long sheet at a high
    /// resolution is more pixels than an image can hold: that is an
    /// <see cref="InvalidOperationException"/> naming the page, rather than a sheet silently broken
    /// in two.</para>
    /// </summary>
    OnePagePerSheet
}
