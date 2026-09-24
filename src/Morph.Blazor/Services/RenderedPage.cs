namespace Morph;

/// <summary>
/// One page rendered for display: the PNG, the page's size and its selectable text. Produced by
/// <see cref="ConversionService.RenderPages"/>; <see cref="DocumentPreview"/> shows a list of them.
/// </summary>
/// <param name="Png">The page image. Always the whole sheet — a text layer cannot line up with a crop.</param>
/// <param name="WidthPoints">Page width in points (1/72 inch).</param>
/// <param name="HeightPoints">Page height in points.</param>
/// <param name="TextLayer">The page's text, positioned to lay over <paramref name="Png"/>.</param>
public sealed record RenderedPage(byte[] Png, double WidthPoints, double HeightPoints, PageTextLayer TextLayer);
