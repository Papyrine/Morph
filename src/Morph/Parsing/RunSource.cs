/// <summary>
/// Where a model <see cref="global::Run"/>'s text came from in the package: <see cref="Run"/> is the ordinal
/// of its <c>w:r</c> among the main document part's runs in document order, and <see cref="Start"/> the
/// position of the run's first character within that <c>w:r</c> (<see cref="SourceRuns"/> defines both).
/// Character <c>i</c> of the run's text sits at <c>Start + i</c> — unless the run is
/// <see cref="Atomic"/>: a note reference draws as its number, however many digits that takes, and is
/// still one position in the source.
///
/// Stamped only when the parser is asked for it (<c>captureSources</c>), which the viewer does so a
/// place on a rendered page can be traced back to the markup a comment or a revision is anchored in.
/// Every other conversion leaves it null and pays nothing for it.
/// </summary>
readonly record struct RunSource(int Run, int Start, bool Atomic = false);
