/// <summary>
/// One section's header and footer set: the default, first-page and even-page variants of each, after
/// Word's inheritance — a section that declares no reference of a type takes the previous section's
/// part of that type, whatever it declares otherwise, so a title-page section three sections deep can
/// show section 1's first-page header (<c>_probe_sect_a</c>, XPS-read 2026-09-06). Which variant a page
/// takes is decided per page: the first-page one on a section's first page when the section sets
/// <c>w:titlePg</c>; the even one on an even page when the document sets <c>w:evenAndOddHeaders</c>
/// (with nothing at all when that section has no even part — Word leaves the page bare rather than
/// falling back to the default); the default otherwise.
/// </summary>
sealed record SectionBands
{
    public HeaderFooterContent? Header { get; init; }

    public HeaderFooterContent? Footer { get; init; }

    public HeaderFooterContent? FirstPageHeader { get; init; }

    public HeaderFooterContent? FirstPageFooter { get; init; }

    public HeaderFooterContent? EvenPageHeader { get; init; }

    public HeaderFooterContent? EvenPageFooter { get; init; }

    /// <summary>
    /// <c>w:settings/w:evenAndOddHeaders</c>: even pages take the even variants (or nothing). Off, the
    /// even parts are ignored and every non-first page takes the default (<c>_probe_sect_c</c>).
    /// </summary>
    public bool EvenAndOddHeaders { get; init; }
}
