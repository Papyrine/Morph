namespace Morph;

/// <summary>
/// Shared base for the per-format export options records (<see cref="HtmlExportOptions"/>,
/// <see cref="MarkdownExportOptions"/>, <see cref="PdfExportOptions"/>, <see cref="ImageExportOptions"/>).
/// Properties here apply to every output format; format-specific knobs live on the derived records.
/// </summary>
public abstract record ExportOptions
{
    /// <summary>
    /// Optional path to a directory containing the font files to use for measurement / embedding.
    /// When set, only fonts from this directory (searched recursively) are used; system/user/Office
    /// font caches and OS-level fallbacks are ignored. Use this to make rendering deterministic
    /// across machines.
    /// </summary>
    public string? FontDirectory { get; init; }

    /// <summary>
    /// Optional delegate to resolve missing fonts. Called with the font family name that could not
    /// be found; return an alternative family, or null to fall through to the curated alias map,
    /// the platform resolver, and finally <see cref="DefaultFont"/>.
    /// <para>
    /// Consulted only once <see cref="FontDirectory"/> / the bundled faces and the host's installed
    /// fonts have both missed, so a family the machine can already serve never reaches it.
    /// </para>
    /// <para>
    /// Shared rather than raster-only because a workbook resolves fonts at PARSE time: Excel's
    /// column-width unit is a glyph of the body font, so the grid every format is built on — HTML
    /// and Markdown included — is sized by whichever face this maps to.
    /// </para>
    /// </summary>
    public Func<string, string?>? FontFallback { get; init; }

    /// <summary>
    /// Overrides the fallback font family used when the source document does not declare a default
    /// run font. When <c>null</c>, <see cref="DefaultFontSettings.DefaultFont"/> is used.
    /// </summary>
    public string? DefaultFont { get; init; }

    /// <summary>
    /// The paper to fall back on when the source document states none — a worksheet with no
    /// <c>pageSetup/@paperSize</c>, or a docx with no <c>w:pgSz</c>. <c>true</c> is US Letter,
    /// <c>false</c> is A4.
    ///
    /// When <c>null</c> the machine's region decides, which is what Word and Excel do (Letter in
    /// North America, A4 elsewhere) but makes the rendered page size depend on where the render
    /// runs. Pin it for output that has to be reproducible across machines — snapshot tests most
    /// of all, since a workbook stating no paper size is the common case rather than the rare one.
    /// </summary>
    public bool? UseLetterPageSize { get; init; }

    /// <summary>
    /// Invoked for every feature the source document contained that couldn't be fully represented
    /// in the chosen output format — unsupported elements, missing fonts, inline images that
    /// failed to decode, etc. Null disables warning emission entirely.
    /// </summary>
    public Action<ExportWarning>? OnWarning { get; init; }
}
