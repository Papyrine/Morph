/// <summary>
/// One wrapped line's measured extent before placement: its ink <see cref="Width"/> and its laid-out
/// <see cref="Height"/> (the tallest run's hhea box under the paragraph's line-spacing rule). The
/// fragmenter turns a paragraph's <see cref="MeasuredLine"/>s into placed lines on pages.
/// </summary>
readonly record struct MeasuredLine(float Width, float Height);
