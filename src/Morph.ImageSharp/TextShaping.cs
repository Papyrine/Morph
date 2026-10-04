/// <summary>
/// Translation between Morph's run model and SixLabors.Fonts shaping options.
/// Pair-kerning and standard ligatures both flow through SixLabors' single
/// <see cref="KerningMode"/> setting, so disabling one disables the other.
/// </summary>
static class TextShaping
{
    public static KerningMode ResolveKerningMode(RunProperties props)
    {
        // The same question the measurer asks: the run kerns when its size reaches the resolved
        // w:kern threshold, and a threshold of zero is kerning off (the parser resolves the
        // cascade, a document with no docDefaults included). The ink then matches the measure.
        if (!CanonicalParagraphMeasurer.KerningEnabled(props))
        {
            return KerningMode.None;
        }

        // w14:ligatures="none" turns off all ligature substitution.
        if (props.Ligatures == LigatureMode.None)
        {
            return KerningMode.None;
        }

        return KerningMode.Standard;
    }
}
