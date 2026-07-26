/// <summary>
/// The fully-paginated output of the layout pass (<c>docs/layout-engine-proposal.md</c>): a document
/// broken into pages once, backend-independently, that each backend then merely paints. Computing this
/// a single time — rather than re-deciding pagination inside three render loops — is what collapses
/// the page-count knife-edges in <c>src/page_counts.md</c>.
/// </summary>
sealed record LaidOutDocument(IReadOnlyList<LaidOutPage> Pages);
