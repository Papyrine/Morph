/// <summary>
/// What Word's Paragraph dialog sets on paragraphs. A null member leaves that property as it is.
/// Indents and spacing are in points; <see cref="FirstLinePoints"/> is a hanging indent when negative.
/// <see cref="LineValue"/> is a multiple of single spacing for <see cref="LineSpacingRule.Auto"/>, and
/// points for <see cref="LineSpacingRule.Exactly"/> and <see cref="LineSpacingRule.AtLeast"/>.
/// </summary>
readonly record struct ParagraphFormat(
    TextAlignment? Alignment = null,
    double? LeftPoints = null,
    double? RightPoints = null,
    double? FirstLinePoints = null,
    double? BeforePoints = null,
    double? AfterPoints = null,
    LineSpacingRule? LineRule = null,
    double? LineValue = null,
    bool? WidowControl = null,
    bool? KeepNext = null,
    bool? KeepLines = null,
    bool? PageBreakBefore = null);
