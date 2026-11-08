namespace Morph;

/// <summary>
/// Options for the vector-text PDF exporter.
/// </summary>
public sealed record PdfExportOptions : ExportOptions
{
    /// <summary>
    /// Scale factor for font width measurements. Values &gt; 1.0 make text wider (earlier line
    /// wrapping); 1.08 tracks Microsoft Word's slightly looser GDI metrics. The default is 1.0,
    /// which leaves layout unchanged.
    /// <para>
    /// Applies to PDF text (wrap points and right/decimal tab stops) and to WordArt rasterized
    /// through a raster backend (see <see cref="RasterizeWordArt"/>). For output consistent with a
    /// raster export of the same document, use the same value here as on the image options.
    /// </para>
    /// </summary>
    public double FontWidthScale { get; init; } = DefaultFontSettings.FontWidthScale;


    /// <summary>
    /// When set, only pages within this 1-based inclusive range are rendered. Null (default)
    /// renders every page.
    /// </summary>
    public PageRange? Pages { get; init; }

    /// <summary>
    /// When true (the default), inline and floating WordArt is rendered with full fidelity
    /// (glyph warps, outline, shadow, glow, reflection) by rasterizing it through a raster backend
    /// — <c>Morph.Skia</c> if it can be loaded, otherwise <c>Morph.ImageSharp</c> — and embedding
    /// the resulting image. This takes effect only when one of those assemblies is deployed
    /// alongside <c>Morph.Pdf</c>; otherwise, and when set to false, WordArt falls back to plain
    /// text occupying the shape's box.
    /// </summary>
    public bool RasterizeWordArt { get; init; } = true;

    /// <summary>
    /// Invoked as the export makes progress, for a host showing "Page x of y" while a long document
    /// converts. Applies to every PDF export — Word, Excel, PowerPoint and HTML alike. Null (the
    /// default) reports nothing.
    /// <para>
    /// The sequence is: <see cref="PdfExportStage.Layout"/> once per page laid out (page 1, 2, … with
    /// no total), then <see cref="PdfExportStage.Paint"/> once per page drawn (page 1 of N up to N of N).
    /// Parsing the source comes before the first call and reports nothing. Page numbers never go
    /// backwards within a stage.
    /// </para>
    /// <para>
    /// It runs synchronously on the converting thread, between pages, so the conversion waits for it:
    /// keep it cheap. A host that writes progress somewhere slow — a database, a message queue — should
    /// throttle or hand off itself. An exception it throws ends the conversion.
    /// </para>
    /// </summary>
    public Action<PdfExportProgress>? OnProgress { get; init; }
}
