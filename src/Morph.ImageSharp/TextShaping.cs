/// <summary>
/// Translation between Morph's run model and SixLabors.Fonts shaping options.
/// Pair-kerning and standard ligatures both flow through SixLabors' single
/// <see cref="KerningMode"/> setting, so disabling one disables the other.
/// </summary>
static class TextShaping
{
    public static KerningMode ResolveKerningMode(RunProperties props)
    {
        // Word only kerns when fontSize >= w:kern threshold. Threshold of 0 = no explicit
        // setting → default kerning behaviour applies.
        if (props.KerningMinFontSizePoints > 0 &&
            props.FontSizePoints < props.KerningMinFontSizePoints)
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
