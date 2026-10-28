/// <summary>
/// <c>w:emboss</c> and <c>w:imprint</c> as Word draws them: each glyph three times, a step of 1.2pt apart
/// on both axes — a grey (#999999) copy where the text would stand, the text in its own colour one step
/// off, and a black copy two steps off; up and left for emboss, down and right for imprint. Word-probed
/// 2026-10-28 (<c>_probe_emboss</c>, Arial at 12, 36 and 72pt, glyph origins and fills off the XPS): the
/// steps were 1.2pt at every size, and wordart's 36pt Impact lines draw the same. The copies are ink only:
/// they carry no text a reader selects (<see cref="PlacedRun.InkOnly"/>).
/// </summary>
static class TextRelief
{
    const float step = 1.2f;

    public static bool Applies(RunProperties properties) =>
        properties.Emboss || properties.Imprint;

    public static IEnumerable<PlacedRun> Draw(PlacedRun run)
    {
        var offset = run.Properties.Emboss ? -step : step;
        var plain = run.Properties with
        {
            Emboss = false,
            Imprint = false
        };
        var copy = plain with
        {
            Underline = false,
            Strikethrough = false,
            DoubleStrikethrough = false,
            BackgroundColorHex = null,
            Border = null
        };

        yield return run with
        {
            Properties = copy with
            {
                ColorHex = "999999"
            },
            InkOnly = true
        };
        yield return run with
        {
            X = run.X + 2 * offset,
            BaselineShift = run.BaselineShift - 2 * offset,
            Properties = copy with
            {
                ColorHex = "000000"
            },
            InkOnly = true
        };
        yield return run with
        {
            X = run.X + offset,
            BaselineShift = run.BaselineShift - offset,
            Properties = plain
        };
    }
}
