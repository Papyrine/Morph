/// <summary>
/// The spacing and indent values a style declares in its own <c>w:pPr</c>, kept nullable so
/// "declared as zero" stays distinguishable from "not declared".
/// </summary>
/// <remarks>
/// This exists for the table-style step of the paragraph cascade. ECMA-376 resolves a paragraph
/// inside a table as <c>docDefaults → table style w:pPr → paragraph style chain → direct w:pPr</c>,
/// so a table style's value applies exactly where the paragraph style chain is silent. The resolved
/// <c>ParagraphProperties</c> cannot answer "is it silent?" — its fields are non-nullable, so a
/// spacing of 0 that came from docDefaults reads the same as one the style set itself.
/// </remarks>
sealed record StyleParagraphSpacing
{
    public double? SpacingBeforePoints { get; init; }
    public double? SpacingAfterPoints { get; init; }
    public double? LineSpacingMultiplier { get; init; }
    public double? LineSpacingPoints { get; init; }
    public LineSpacingRule? LineSpacingRule { get; init; }
    public double? LeftIndentPoints { get; init; }
    public double? RightIndentPoints { get; init; }

    public bool DeclaresAnything =>
        SpacingBeforePoints != null ||
        SpacingAfterPoints != null ||
        LineSpacingRule != null ||
        LeftIndentPoints != null ||
        RightIndentPoints != null;

    /// <summary>
    /// Layers <paramref name="over"/> on top of this one — used both to walk a style's
    /// <c>w:basedOn</c> chain and to combine a table style with a paragraph style.
    /// </summary>
    public StyleParagraphSpacing Layer(StyleParagraphSpacing? over)
    {
        if (over == null)
        {
            return this;
        }

        return new()
        {
            SpacingBeforePoints = over.SpacingBeforePoints ?? SpacingBeforePoints,
            SpacingAfterPoints = over.SpacingAfterPoints ?? SpacingAfterPoints,
            // The three line values move together: a w:line re-declaration replaces the rule and
            // both magnitudes, so splitting them would let an Auto multiplier survive under a
            // later Exactly rule.
            LineSpacingMultiplier = over.LineSpacingRule != null ? over.LineSpacingMultiplier : LineSpacingMultiplier,
            LineSpacingPoints = over.LineSpacingRule != null ? over.LineSpacingPoints : LineSpacingPoints,
            LineSpacingRule = over.LineSpacingRule ?? LineSpacingRule,
            LeftIndentPoints = over.LeftIndentPoints ?? LeftIndentPoints,
            RightIndentPoints = over.RightIndentPoints ?? RightIndentPoints
        };
    }
}
