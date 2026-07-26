/// <summary>
/// One page of a <see cref="LaidOutDocument"/>: its 1-based number, the <see cref="PageSettings"/> in
/// force (section-aware), and the <see cref="PlacedItem"/>s placed on it in paint order — text lines
/// (<see cref="PlacedLine"/>) and table rows (<see cref="PlacedTableRow"/>) so far, with images, rules
/// and shapes joining as further placed-item kinds in later slices.
/// </summary>
sealed record LaidOutPage(int Number, PageSettings Settings, IReadOnlyList<PlacedItem> Items);
