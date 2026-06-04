using Morph;

/// <summary>
/// Provides configurable font rendering settings to better match Microsoft Word.
/// All settings are process-wide and must be configured before the first render;
/// attempts to change them afterwards throw <see cref="InvalidOperationException"/>.
/// </summary>
static class DefaultFontSettings
{
    /// <summary>
    /// Default fallback font family used when a DOCX document does not specify one in
    /// <c>docDefaults</c>. Aptos matches modern Word (Microsoft 365's default since 2023)
    /// — and Morph ships the four standard Aptos faces as embedded resources so this
    /// resolves on every host, including Linux/macOS machines that don't have it
    /// installed. The bundled bytes are decoded once per <c>TFont</c> backend and
    /// seeded into the <see cref="FontResolver{TFont}"/> cache.
    /// </summary>
    const string builtInDefaultFont = "Aptos";

    static double fontWidthScale = 1.0;
    static string defaultFont = builtInDefaultFont;
    static bool renderOccurred;
    static bool deterministicRendering;

    /// <summary>
    /// Gets or sets the font width scale factor for text measurements.
    /// Values > 1.0 make text appear wider (causes earlier line wrapping).
    /// Default is 1.0. Use 1.07 to better match Microsoft Word's text rendering.
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
