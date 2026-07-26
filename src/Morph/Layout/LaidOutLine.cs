/// <summary>
/// One wrapped line as the canonical measurer lays it out for painting: the line box (<see cref="Width"/>
/// and the laid-out <see cref="Height"/>), the <see cref="Ascent"/> from the line's top down to the text
/// baseline, and the <see cref="Text"/> plus its dominant <see cref="FontProperties"/>. The first painter
/// slice carries a single font per line; a later slice replaces <see cref="Text"/>/<see cref="FontProperties"/>
/// with the run segments of a mixed-format line. Sits between the pure-measurement <see cref="MeasuredLine"/>
/// (width + height) and the placed <see cref="PlacedLine"/> the fragmenter emits.
/// </summary>
readonly record struct LaidOutLine(float Width, float Height, float Ascent, string Text, RunProperties FontProperties);
