/// <summary>
/// Marks a run as the result of a page-numbering field whose value must be computed per page
/// at render time (Word's cached result is a fixed string that is wrong on every page but the
/// one it was computed for). <see cref="Run.PageField"/> carries this; the paginated renderers
/// substitute the live value, while the HTML/Markdown exporters emit the cached text unchanged.
/// </summary>
enum PageFieldKind
{
    /// <summary>Not a page field — render the run's text verbatim.</summary>
    None,

    /// <summary>w:fldChar/w:fldSimple <c>PAGE</c> — the current page number.</summary>
    Page,

    /// <summary>w:fldChar/w:fldSimple <c>NUMPAGES</c> — the total page count.</summary>
    NumberOfPages,

    /// <summary>w:fldChar/w:fldSimple <c>SECTIONPAGES</c> — pages in the current section
    /// (approximated by the total page count until section-scoped pagination is modelled).</summary>
    SectionPages
}
