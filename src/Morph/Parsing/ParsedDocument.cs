/// <summary>
/// Represents a parsed DOCX document.
/// </summary>
sealed class ParsedDocument
{
    public required PageSettings PageSettings { get; init; }
    public required IReadOnlyList<DocumentElement> Elements { get; init; }
    public HeaderFooterContent? Header { get; init; }
    public HeaderFooterContent? Footer { get; init; }
    public HeaderFooterContent? FirstPageHeader { get; init; }
    public HeaderFooterContent? FirstPageFooter { get; init; }

    /// <summary>
    /// Header for even-numbered pages when the document opts in via w:evenAndOddHeaders.
    /// Null when even-page headers aren't enabled — pages 2/4/6 fall back to <see cref="Header"/>.
    /// </summary>
    public HeaderFooterContent? EvenPageHeader { get; init; }

    /// <summary>
    /// Footer for even-numbered pages when the document opts in via w:evenAndOddHeaders.
    /// Null when even-page footers aren't enabled.
    /// </summary>
    public HeaderFooterContent? EvenPageFooter { get; init; }

    /// <summary>
    /// Document-level hyphenation settings.
    /// </summary>
    public HyphenationSettings Hyphenation { get; init; } = new();

    /// <summary>
    /// Theme colors from the document theme.
    /// </summary>
    public ThemeColors? ThemeColors { get; init; }

    /// <summary>
    /// Theme fonts from the document theme.
    /// </summary>
    public ThemeFonts? ThemeFonts { get; init; }

    /// <summary>
    /// Word compatibility settings from the document.
    /// </summary>
    public CompatibilitySettings Compatibility { get; init; } = new();

    /// <summary>
    /// Named bookmarks in the document body (w:bookmarkStart). Bookmarks are invisible —
    /// they exist for cross-reference fields and hyperlink anchors.
    /// </summary>
    public IReadOnlyList<Bookmark> Bookmarks { get; init; } = [];

    /// <summary>
    /// Reviewer comments parsed from word/comments.xml. Comments are not rendered today.
    /// </summary>
    public IReadOnlyList<Comment> Comments { get; init; } = [];

    /// <summary>
    /// Tracked change revisions (w:ins / w:del). Currently captured in the model only —
    /// renderers neither show insertions in revision colour nor strike out deletions.
    /// </summary>
    public IReadOnlyList<TrackedChange> TrackedChanges { get; init; } = [];

    /// <summary>
    /// Document protection / editing-restriction settings (w:documentProtection).
    /// Has no rendering effect — exposed for consumers that care about read-only state.
    /// </summary>
    public DocumentProtectionSettings Protection { get; init; } = new();

    /// <summary>
    /// Field codes (w:fldChar/w:instrText) captured from the document body. Renderers continue
    /// to emit each field's cached result text inline; this list lets consumers see which fields
    /// are present (PAGE, TOC, REF, HYPERLINK, etc.) without re-walking the OOXML.
    /// </summary>
    public IReadOnlyList<FieldCode> FieldCodes { get; init; } = [];

    /// <summary>
    /// True when a <c>NUMPAGES</c> or <c>SECTIONPAGES</c> field appears anywhere (body, header or
    /// footer). Those fields need the final page total, which a single-pass renderer only knows
    /// after laying the document out — the raster/PDF converters run a gated counting pass first
    /// when this is set. <c>PAGE</c> alone does not set it (the current page number is known during
    /// the normal pass).
    /// </summary>
    public bool RequiresTotalPageCount { get; init; }

    /// <summary>
    /// True when every page field's cached text is already correct for the instance that carries
    /// it — a presentation caches each slide's own number in its <c>a:fld</c> — so the reflowing
    /// text exporters keep the cached text instead of evaluating the document as a single page.
    /// </summary>
    public bool PageFieldsPreEvaluated { get; init; }

    /// <summary>
    /// Footnotes from word/footnotes.xml. Renderer does not yet emit them at the page bottom.
    /// </summary>
    public IReadOnlyList<Footnote> Footnotes { get; init; } = [];

    /// <summary>
    /// Endnotes from word/endnotes.xml. Renderer does not yet emit them at the document end.
    /// </summary>
    public IReadOnlyList<Endnote> Endnotes { get; init; } = [];

    /// <summary>
    /// Embedded OLE objects (w:object / o:OLEObject) referenced from the document body.
    /// Renderer does not yet draw the embedded payload — these are captured for inspection only.
    /// </summary>
    public IReadOnlyList<EmbeddedObject> EmbeddedObjects { get; init; } = [];

    /// <summary>
    /// Watermarks extracted from the document's header parts. Drawn behind body content on every page.
    /// </summary>
    public IReadOnlyList<Watermark> Watermarks { get; init; } = [];

    /// <summary>
    /// Presence flags for advanced OOXML features that the renderer doesn't yet draw.
    /// Lets consumers decide whether to fall back to Word for the document.
    /// </summary>
    public DocumentFeatures Features { get; init; } = new();
}