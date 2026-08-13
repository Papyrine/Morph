/// <summary>
/// Provides configurable font rendering settings to better match Microsoft Word.
/// All settings are process-wide and must be configured before the first render;
/// attempts to change them afterwards throw <see cref="InvalidOperationException"/>.
/// </summary>
static class DefaultFontSettings
{
    /// <summary>
    /// Default fallback font family used when a DOCX document does not specify one in
    /// <c>docDefaults</c>. Morph ships the four standard Aptos faces as embedded resources so this
    /// resolves on every host, including Linux/macOS machines that don't have it installed. The
    /// bundled bytes are decoded once per <c>TFont</c> backend and seeded into the
    /// <see cref="FontResolver{TFont}"/> cache.
    ///
    /// <para><b>Word actually uses CALIBRI 12pt here, not Aptos.</b> Probed 2026-08-13 with a bare
    /// three-part package carrying one paragraph and no <c>w:rFonts</c> anywhere: Word rendered the
    /// sample string 632px wide and 21px tall at 150 DPI, matching its own explicit Calibri 12
    /// exactly on BOTH axes (Aptos 12 is 665x20, Times 12 is 629x23). One variant per page, because
    /// a first probe with all variants on one page produced a line-to-variant mapping that did not
    /// survive a 12/11 scaling check. Aptos is Word's default for NEW documents, which always
    /// declare it in <c>docDefaults</c> — so that default never reaches this constant.
    ///
    /// It stays Aptos anyway, and the reason is NOT that Calibri breaks pagination — an earlier note
    /// here claimed four scenarios lost Word's page count under Calibri and that was WRONG, an
    /// artifact of counting Verify's `.received.` files, which exist only for pages that DIFFER.
    /// Re-measured from `ResultingPageCount` in the result JSON: zero scenarios change page count.
    ///
    /// The real reason is fidelity, and it is narrow: switching moves mean AE from 0.04142 to
    /// 0.04158 over the 150 scenarios it touches. Tables and short text improve sharply
    /// (complex_document -0.048, table_default_style -0.037, table_borders -0.009) while flowing
    /// text regresses (complex_spacing +0.102, multiple_pages +0.031). That split is the whole
    /// story: Word grid-fits Calibri's advances away from the font file's — per glyph, by up to
    /// +4.6% at 12pt (see <c>src/todo.md</c> #43) — so Morph cannot track Word's Calibri, while its
    /// Aptos, Times and Calibri-at-other-sizes all sit within ~1%. Aptos is therefore the font
    /// Morph renders most faithfully to Word even though Calibri is what Word would have used.
    /// Revisit if hinted advances are ever modelled.</para>
    /// </summary>
    const string builtInDefaultFont = "Aptos";

    static double fontWidthScale = 1.0;
    static string defaultFont = builtInDefaultFont;
    static bool renderOccurred;
    static bool deterministicRendering;

    /// <summary>
    /// Gets or sets the font width scale factor for text measurements.
    /// Values > 1.0 make text appear wider (causes earlier line wrapping).
    /// Default is 1.0. Use 1.08 to better match Microsoft Word's text rendering.
    /// Must be set before the first render; attempts to change it after any
    /// conversion has started will throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public static double FontWidthScale
    {
        get => fontWidthScale;
        set
        {
            ThrowIfRendered(nameof(FontWidthScale));
            fontWidthScale = value;
        }
    }

    /// <summary>
    /// Gets or sets the process-wide default fallback font family, used when a DOCX
    /// document does not declare a default run font. Defaults to <c>Aptos</c> (Word's
    /// default since 2023); the four standard faces ship inside Morph.dll as embedded
    /// resources so the default resolves on every host. Must be set before the first
    /// render; attempts to change it after any conversion has started will throw
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <remarks>
    /// To override per-conversion without affecting other callers, use
    /// <see cref="ExportOptions.DefaultFont"/> instead.
    /// </remarks>
    public static string DefaultFont
    {
        get => defaultFont;
        set
        {
            ThrowIfRendered(nameof(DefaultFont));
            defaultFont = value;
        }
    }

    /// <summary>
    /// When <c>true</c>, backends disable font hinting and sub-pixel positioning
    /// for glyph rendering, falling back to integer-positioned greyscale anti-aliasing.
    /// Produces output that's identical across machines and font rasterizer versions
    /// (at the cost of slightly softer text), making scenario tests stable on CI.
    /// Intended for test harnesses; leave <c>false</c> in production.
    /// </summary>
    public static bool DeterministicRendering
    {
        get => deterministicRendering;
        set
        {
            ThrowIfRendered(nameof(DeterministicRendering));
            deterministicRendering = value;
        }
    }

    /// <summary>
    /// Invoked by converters on the first render call to lock all static
    /// settings on <see cref="DefaultFontSettings"/> against further changes.
    /// </summary>
    internal static void MarkRenderOccurred() => renderOccurred = true;

    /// <summary>
    /// Resets all settings to their default values. Intended for tests.
    /// </summary>
    public static void ResetToDefault()
    {
        fontWidthScale = 1.0;
        defaultFont = builtInDefaultFont;
        deterministicRendering = false;
        renderOccurred = false;
    }

    static void ThrowIfRendered(string setting)
    {
        if (renderOccurred)
        {
            throw new InvalidOperationException(
                $"DefaultFontSettings.{setting} cannot be changed after a render has started. Set it once during application startup, or use the matching per-format export-options property per conversion.");
        }
    }
}
