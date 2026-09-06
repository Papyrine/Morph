/// <summary>
/// An inline picture's <c>wp:effectExtent</c> in points: the room Word reserves around the picture's
/// extent for its outline, shadow and other effects. XPS-read on <c>_probe_picln2</c> (2026-09-06,
/// a 150×112.5pt picture at a 72pt margin): the picture draws at the paragraph origin plus
/// (<see cref="Left"/>, <see cref="Top"/>) — 84pt for a 12pt extent, 102pt for 30pt — and the line
/// reserves the extent plus all four edges (the caption under it moves down by exactly the bottom
/// edge: 12, 24 and 30pt for those extents), whether or not the picture carries an <c>a:ln</c> and
/// however wide that line is. newsletters/01's photos declare 10.5pt each side around a 7pt frame,
/// which is why Word's captions sat 25pt lower than the engine's. Absent or all-zero parses to null.
/// </summary>
sealed record ImageEffectExtent(double Left, double Top, double Right, double Bottom);
