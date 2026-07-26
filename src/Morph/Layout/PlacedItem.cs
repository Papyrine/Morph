/// <summary>
/// Base for anything the fragmenter places on a <see cref="LaidOutPage"/>: an absolute bounding box in
/// points from the page's top-left. Concrete kinds are <see cref="PlacedLine"/> (a wrapped text line)
/// and <see cref="PlacedTableRow"/> (a table row); images, rules and shapes join as their slices land.
/// </summary>
abstract record PlacedItem(float X, float Y, float Width, float Height);
