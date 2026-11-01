/// <summary>
/// The second plot of a pie of pie or a bar of pie (<c>c:ofPieChart</c>): which points it breaks out
/// of the first pie, and its shape and size. <see cref="SecondPlot"/> lists the points by index, in
/// order; the first pie draws them together as one slice.
/// </summary>
sealed record OfPieSplit(bool Bar, IReadOnlyList<int> SecondPlot, double SecondSizePercent, double GapWidthPercent);
