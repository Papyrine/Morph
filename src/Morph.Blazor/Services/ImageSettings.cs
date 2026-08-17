namespace Morph;

/// <summary>
/// User-tunable knobs for the PNG export, surfaced in the converter's image-options panel. Maps to
/// <see cref="ImageExportOptions"/> in <see cref="ConversionService"/>.
/// </summary>
public sealed class ImageSettings
{
    /// <summary>Render resolution in DPI. Default 150 matches <see cref="ImageExportOptions.Dpi"/>.</summary>
    public int Dpi { get; set; } = 150;

    /// <summary>
    /// How much of the paper page each image covers. Default <see cref="PageCrop.FullPage"/> matches
    /// <see cref="ImageExportOptions.Crop"/>.
    /// </summary>
    public PageCrop Crop { get; set; } = PageCrop.FullPage;
}
