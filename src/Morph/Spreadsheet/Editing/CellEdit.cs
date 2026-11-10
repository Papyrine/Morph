/// <summary>
/// One cell to set, by its row and column from 1, to what was typed into it — a formula with its
/// <c>=</c>, a number, text — or to nothing, which clears what it holds and keeps how it looks.
/// </summary>
sealed record CellEdit(int Row, int Column, string? Input);
