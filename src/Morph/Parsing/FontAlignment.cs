/// <summary>
/// Where a paragraph sets its smaller runs against the line's largest (<c>w:textAlignment</c>).
///
/// Word-probed (<c>font_alignment</c>, 2026-10-21, XPS baselines): <see cref="Baseline"/> (and auto)
/// shares the baseline; otherwise a run moves by a fixed fraction of how much smaller it is than the
/// line's largest — up 0.895 of the difference for <see cref="Top"/>, up 0.34 for <see cref="Center"/>,
/// down 0.105 for <see cref="Bottom"/>. Measured on Arial and Times New Roman at 6pt against 24pt and
/// 8pt against 40pt, the fractions agree within 0.015 across faces whose ascents differ widely: Word
/// places the runs on their em boxes, not by the faces' metrics.
/// </summary>
enum FontAlignment
{
    Baseline,
    Top,
    Center,
    Bottom
}
