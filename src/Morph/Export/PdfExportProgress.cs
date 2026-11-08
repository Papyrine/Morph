namespace Morph;

/// <summary>
/// How far a PDF export has got, delivered to <see cref="PdfExportOptions.OnProgress"/> so a host can
/// show "Page x of y" while a long document converts.
/// </summary>
/// <param name="Stage">What the export is doing: laying the pages out, or drawing them.</param>
/// <param name="Page">
/// The pages finished so far in this stage. During <see cref="PdfExportStage.Layout"/> it counts the
/// pages laid out; during <see cref="PdfExportStage.Paint"/> the pages drawn, 1 to <paramref name="Total"/>.
/// </param>
/// <param name="Total">
/// How many pages there will be, or <c>null</c> while it is not yet known — that is, throughout
/// <see cref="PdfExportStage.Layout"/>. During <see cref="PdfExportStage.Paint"/> it is the number of
/// pages the PDF will hold: every page, or only those in <see cref="PdfExportOptions.Pages"/>.
/// </param>
public sealed record PdfExportProgress(PdfExportStage Stage, int Page, int? Total);

/// <summary>The stages a PDF export reports through <see cref="PdfExportOptions.OnProgress"/>.</summary>
public enum PdfExportStage
{
    /// <summary>
    /// Breaking the document into pages. The page total is not known until this ends, so progress here
    /// carries no total.
    /// </summary>
    Layout,

    /// <summary>Drawing the laid-out pages into the PDF, one at a time, with the total known.</summary>
    Paint
}
