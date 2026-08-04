/// <summary>
/// One warped WordArt shape placed on a page: its resolved box in points from the page's top-left and the
/// source <see cref="IWordArtVisual"/> a painter reads for the text, font, fill, outline, effects and warp
/// preset. Unwarped WordArt does not come through here — the fragmenter lays that out as box chrome
/// (<see cref="PlacedShape"/>) plus centred text (<see cref="PlacedLine"/>), since it is just Word's inline
/// text box. A warp is a single opaque figure instead — text on an arc, an envelope, a wave — so it stays
/// one item and the painter rasterizes it through its backend's <c>IWordArtRasterizer</c>, reusing the warp
/// geometry rather than reimplementing it.
/// </summary>
sealed record PlacedWordArt(
    float X,
    float Y,
    float Width,
    float Height,
    IWordArtVisual Visual) : PlacedItem(X, Y, Width, Height);
