/// <summary>
/// Translation between Morph's run model and SixLabors.Fonts shaping options.
/// Pair-kerning and standard ligatures both flow through SixLabors' single
/// <see cref="KerningMode"/> setting, so disabling one disables the other. Kerning decides: it moves
/// every pair of a kerned run, where a ligature narrows a few glyphs by a fraction of a point.
/// </summary>
static class TextShaping
{
    // The same question the measurer asks: the run kerns when its size reaches the resolved w:kern
    // threshold, and a threshold of zero is kerning off (the parser resolves the cascade, a document
    // with no docDefaults included). The ink then matches the measure. Ligatures cannot be asked for
    // apart from it, so a run whose ligatures are off (RunProperties.Ligatures, None below
    // compatibility mode 15) but which kerns still draws them; one that does not kern draws none.
    public static KerningMode ResolveKerningMode(RunProperties props) =>
        CanonicalParagraphMeasurer.KerningEnabled(props) ? KerningMode.Standard : KerningMode.None;
}
