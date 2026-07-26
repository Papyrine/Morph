/// <summary>
/// One page of a <see cref="LaidOutDocument"/>: its 1-based number, the <see cref="PageSettings"/> in
/// force (section-aware), and the lines placed on it in paint order. The first fragmenter slice places
/// text lines; images, rules, shapes and table cells attach as further placed-item kinds in later
/// slices.
/// </summary>
sealed record LaidOutPage(int Number, PageSettings Settings, IReadOnlyList<PlacedLine> Lines);
