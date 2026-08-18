/// <summary>
/// Provides configurable font rendering settings to better match Microsoft Word.
/// All settings are process-wide and must be configured before the first render;
/// attempts to change them afterwards throw <see cref="InvalidOperationException"/>.
/// </summary>
static class DefaultFontSettings
{
    /// <summary>
    /// Factory default for <see cref="DefaultFont"/>: the resolver-safe last resort. Morph ships
    /// the four standard Aptos faces as embedded resources, so this resolves on every host,
    /// including Linux/macOS machines with no fonts installed - which is why it stays Aptos even
    /// though Word's own built-in default for a style-less document is Calibri 12pt.
    ///
    /// <para>The parser's built-in family for a style-less document lives in
    /// <c>DocumentParser.builtInDefaultFontFamily</c> and only applies when neither
    /// <see cref="ExportOptions.DefaultFont"/> nor this setting has been customized. Word's own
    /// built-in there is Calibri 12pt, and Word's per-glyph Calibri advance model is measured and
    /// tooled (<see cref="FontMetrics.WordAdvances"/>, <c>scripts/generate-word-advances.py</c>) -
    /// but both the advance sidecars and the family flip are parked until kerning is modelled,
    /// because without it they measured worse against Word than the linear track whose -2.4%
    /// narrowness had been cancelling the missing kerning. See <c>src/todo.md</c> #43.</para>
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
    /// Gets or sets the process-wide default fallback font family: the last resort the font
    /// resolvers fall back to for an unresolvable face, and - when customized - the default run
    /// font for a DOCX document that does not declare one (left uncustomized, such documents get
    /// Word's own built-in, Calibri 12pt, with this family as the resolver fallback behind it).
    /// Defaults to <c>Aptos</c>; the four standard faces ship inside Morph.dll as embedded
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
    /// <see cref="DefaultFont"/> when a caller has customized it, else null - so
    /// <c>DocumentParser</c> can tell "the user chose a family" (honor it for style-less
    /// documents) from "factory default" (apply the parser's built-in). A caller
    /// explicitly setting the factory value is indistinguishable from not setting it, which is
    /// benign: they asked for the resolver default that already backs the built-in.
    /// </summary>
    internal static string? CustomizedDefaultFont =>
        defaultFont == builtInDefaultFont ? null : defaultFont;

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
