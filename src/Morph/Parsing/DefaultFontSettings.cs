/// <summary>
/// Provides configurable font rendering settings to better match Microsoft Word.
/// All settings are process-wide and must be configured before the first render;
/// attempts to change them afterwards throw <see cref="InvalidOperationException"/>.
/// </summary>
static class DefaultFontSettings
{
    /// <summary>
    /// Default fallback font family used when a DOCX document does not specify one in
    /// <c>docDefaults</c>. Georgia is chosen because it ships with Windows and macOS
    /// (and is widely available on Linux via fontconfig), unlike Aptos (Word 2019+ only).
    /// Using a universally-available family keeps rendering working on any host.
    /// </summary>
    const string builtInDefaultFont = "Georgia";

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
    /// document does not declare a default run font. Defaults to <c>Georgia</c>, which
    /// is more commonly installed across operating systems than Word's modern default
    /// (Aptos). Must be set before the first render; attempts to change it after any
    /// conversion has started will throw <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <remarks>
    /// To override per-conversion without affecting other callers, use
    /// <see cref="WordRender.ConversionOptions.DefaultFont"/> instead.
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
                $"DefaultFontSettings.{setting} cannot be changed after a render has started. Set it once during application startup, or use the matching ConversionOptions property per conversion.");
        }
    }
}
