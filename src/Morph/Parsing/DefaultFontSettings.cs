/// <summary>
/// Provides configurable font rendering settings to better match Microsoft Word.
/// </summary>
static class DefaultFontSettings
{
    static double fontWidthScale = 1.0;

    /// <summary>
    /// Gets or sets the font width scale factor for text measurements.
    /// Values > 1.0 make text appear wider (causes earlier line wrapping).
    /// Default is 1.0. Use 1.07 to better match Microsoft Word's text rendering.
    /// </summary>
    public static double FontWidthScale
    {
        get => fontWidthScale;
        set => fontWidthScale = value;
    }

    /// <summary>
    /// Resets FontWidthScale to the default value (1.0).
    /// </summary>
    public static void ResetToDefault() => fontWidthScale = 1.0;
}