/// <summary>
/// The spacing an edit sets on paragraphs. A null member leaves that spacing as it is. <see cref="Line"/>
/// is a multiple of single spacing (1.15, 1.5, 2); the others are in points.
/// </summary>
readonly record struct ParagraphSpacing(double? Line = null, double? BeforePoints = null, double? AfterPoints = null);
