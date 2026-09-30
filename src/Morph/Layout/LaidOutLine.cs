/// <summary>
/// One wrapped line as the canonical measurer lays it out for painting: the line box (<see cref="Width"/>
/// and the laid-out <see cref="Height"/>), the <see cref="Ascent"/> from the line's top down to the text
/// baseline (the tallest run or image), the <see cref="Runs"/> to paint left to right — one per source
/// run that falls on the line, so a mixed-format line carries several — and the inline
/// <see cref="Images"/> on the line. An empty paragraph's mark line has no runs. Sits between the
/// pure-measurement <see cref="MeasuredLine"/> (width + height) and the placed <see cref="PlacedLine"/>
/// the fragmenter emits. <see cref="FootnoteReferenceIds"/> / <see cref="EndnoteReferenceIds"/> name
/// the notes the line cites, in order (null when it cites none): the fragmenter opens a footnote's
/// page-bottom area when its citing line lands, and flows the endnotes after the body.
/// <see cref="Overhang"/> is how much of the box's bottom may hang past the bottom margin when the line
/// is the last in its region: the leading a <c>lineRule="auto"</c> multiple above 1 adds under the
/// baseline. Zero for every other line.
/// </summary>
readonly record struct LaidOutLine(float Width, float Height, float Ascent, IReadOnlyList<LaidOutRun> Runs, IReadOnlyList<LaidOutImage> Images, IReadOnlyList<string>? FootnoteReferenceIds = null, IReadOnlyList<string>? EndnoteReferenceIds = null, float Overhang = 0);
