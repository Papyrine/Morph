namespace Morph;

/// <summary>
/// User-tunable knobs for the PNG export, surfaced in the converter's image-options panel. Maps to
/// <see cref="ImageExportOptions"/> in <see cref="ConversionService"/>.
/// </summary>
public sealed class ImageSettings
{
    /// <summary>Render resolution in DPI. Default 150 matches <see cref="ImageExportOptions.Dpi"/>.</summary>
    public int Dpi { get; set; } = 150;
}
