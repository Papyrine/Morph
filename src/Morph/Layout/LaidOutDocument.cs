/// <summary>
/// The fully-paginated output of the layout pass (<c>docs/layout-engine.md</c>): a document
/// broken into pages once, backend-independently, that each backend then merely paints. Computing this
/// a single time — rather than re-deciding pagination inside three render loops — is what collapses
/// the page-count knife-edges in <c>src/page_counts.md</c>.
/// </summary>
sealed record LaidOutDocument(IReadOnlyList<LaidOutPage> Pages)
{
    /// <summary>
    /// The same document restricted to <paramref name="range"/> (1-based, inclusive), or unchanged
    /// when it is null.
    ///
    /// Safe to apply only because pagination is already fully resolved: the fragmenter has assembled
    /// each page's header and footer bands and baked its page-number fields against the true total,
    /// so dropping pages here changes which are painted and nothing else. A range extending past the
    /// last page simply keeps everything from its start.
    /// </summary>
    public LaidOutDocument Restrict(PageRange? range) =>
        range is { } bounds
            ? new(Pages.Where(_ => bounds.Contains(_.Number)).ToArray())
            : this;
}
