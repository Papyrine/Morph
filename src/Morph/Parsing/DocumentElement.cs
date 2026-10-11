/// <summary>
/// Base class for document elements.
/// </summary>
abstract class DocumentElement
{
    /// <summary>
    /// Whether the element is part of the document but left off the printed page: a spreadsheet
    /// drawing whose anchor says <c>fPrintsWithSheet="0"</c>, which is how a workbook marks its
    /// on-screen navigation buttons. The paged layout skips it; the HTML and Markdown exports, which
    /// are not print, keep it. Internal rather than public so that it is no part of the model
    /// snapshots, which print an element's public state and would carry it on every element.
    /// </summary>
    internal bool NotPrinted { get; init; }
}
