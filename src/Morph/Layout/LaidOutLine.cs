/// <summary>
/// One wrapped line as the canonical measurer lays it out for painting: the line box (<see cref="Width"/>
/// and the laid-out <see cref="Height"/>), the <see cref="Ascent"/> from the line's top down to the text
/// baseline (the tallest run's ascent), and the <see cref="Runs"/> to paint left to right — one per
/// source run that falls on the line, so a mixed-format line carries several. An empty paragraph's mark
/// line has no runs. Sits between the pure-measurement <see cref="MeasuredLine"/> (width + height) and the
/// placed <see cref="PlacedLine"/> the fragmenter emits.
/// </summary>
readonly record struct LaidOutLine(float Width, float Height, float Ascent, IReadOnlyList<LaidOutRun> Runs);
