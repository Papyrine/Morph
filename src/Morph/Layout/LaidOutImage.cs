/// <summary>
/// An inline image on a wrapped line as the canonical measurer lays it out: its drawable
/// <see cref="Data"/> (raster bytes; an SVG's raster fallback), display <see cref="Width"/>/
/// <see cref="Height"/> in points, and <see cref="X"/> offset from the line's left edge. The image sits
/// as an unbreakable box in the flow — its width counts toward the line width and its height can grow the
/// line. Positioned with its bottom on the text baseline by the fragmenter.
/// </summary>
readonly record struct LaidOutImage(float X, float Width, float Height, byte[] Data);
