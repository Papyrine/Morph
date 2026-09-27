/// <summary>
/// A place between two characters of the main document part: <see cref="Offset"/> positions into the
/// <c>w:r</c> with ordinal <see cref="Run"/>, in the coordinates <see cref="SourceRuns"/> defines. Offset 0
/// is before the run's first character, and the run's length is after its last.
/// </summary>
readonly record struct SourcePosition(int Run, int Offset);
