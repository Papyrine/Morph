namespace WordRender;

/// <summary>
/// Options for document conversion.
/// </summary>
/// <param name="Dpi">Image resolution in dots per inch. Default is 150.</param>
/// <param name="FontWidthScale">Scale factor for font width measurements. Default uses DefaultFontSettings.FontWidthScale.
/// Use values > 1.0 to make text wider (causes earlier line wrapping).
/// A value of 1.07 better matches Microsoft Word's text rendering.</param>
public sealed record ConversionOptions
{
    public int Dpi { get; init; } = 150;
    public double FontWidthScale { get; init; } = DefaultFontSettings.FontWidthScale;
}
