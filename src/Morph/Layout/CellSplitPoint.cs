/// <summary>
/// How far through a table cell's content a page break fell, so the cell can continue on the next page:
/// the index of the first element still to place and, when the break landed inside a paragraph, the index
/// of that paragraph's first unplaced line. The default value is the cell's start.
/// </summary>
readonly record struct CellSplitPoint(int ElementIndex, int LineIndex);
