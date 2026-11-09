namespace Morph;

/// <summary>
/// How far a paginated export has got, delivered to <see cref="PdfExportOptions.OnProgress"/> or
/// <see cref="ImageExportOptions.OnProgress"/> so a host can show "Page x of y" while a long document
/// converts.
/// </summary>
/// <param name="Stage">What the export is doing: laying the pages out, or drawing them.</param>
/// <param name="Page">
/// The pages finished so far in this stage. During <see cref="ExportStage.Layout"/> it counts the
/// pages laid out; during <see cref="ExportStage.Paint"/> the pages drawn, 1 to <paramref name="Total"/>.
/// </param>
/// <param name="Total">
/// How many pages there will be, or <c>null</c> while it is not yet known — that is, throughout
/// <see cref="ExportStage.Layout"/>. During <see cref="ExportStage.Paint"/> it is the number of
/// pages the export draws: every page, or only those in the options' <c>Pages</c> range.
/// </param>
public sealed record ExportProgress(ExportStage Stage, int Page, int? Total);

/// <summary>The stages a paginated export reports through its options' <c>OnProgress</c>.</summary>
public enum ExportStage
{
    /// <summary>
    /// Breaking the document into pages. The page total is not known until this ends, so progress here
    /// carries no total.
    /// </summary>
    Layout,

    /// <summary>Drawing the laid-out pages, one at a time, with the total known.</summary>
    Paint
}
