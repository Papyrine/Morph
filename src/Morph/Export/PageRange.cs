namespace Morph;

/// <summary>
/// A 1-based, inclusive page range used to limit which pages of a document are rendered. Pass to
/// <see cref="PdfExportOptions.Pages"/> or <see cref="ImageExportOptions.Pages"/>.
/// </summary>
/// <param name="Start">First page to include (1-based).</param>
/// <param name="End">Last page to include (1-based, inclusive). May be larger than the document's
/// page count, in which case rendering stops at the last page.</param>
public readonly record struct PageRange(int Start, int End)
{
    /// <summary>Convenience for a single-page range.</summary>
    public static PageRange Single(int page) => new(page, page);

    /// <summary>Whether <paramref name="pageNumber"/> (1-based) is included in this range.</summary>
    public bool Contains(int pageNumber) => pageNumber >= Start && pageNumber <= End;
}
