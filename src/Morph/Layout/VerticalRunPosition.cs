/// <summary>
/// Where a superscript or subscript run (<c>w:vertAlign</c>) sits and how big it draws — XPS-read on
/// <c>_probe_subsup</c> / <c>_probe_subsup2</c> (2026-09-05; Calibri at 12/24/36/48/72/96pt, Times
/// New Roman, Arial, Verdana, Georgia, Segoe UI and Cambria at 96pt, Times and Arial at 48pt):
/// <list type="bullet">
/// <item><b>Size</b>: 65% of the run size for every face measured but Cambria (60%) — 12pt draws at
/// 7.8, 48 at 31.2, 96 at 62.4 — and never the 58% LibreOffice convention this replaced. Word rounds
/// that em to its 120-dpi grid; Morph keeps the nominal value, as it does for every glyph size.</item>
/// <item><b>Superscript</b>: raised by a third of the run size. Times, Arial and Verdana read exactly
/// 0.35 × size, Calibri 0.333 at every size from 24pt up (53px of 160 at 96pt), Segoe UI 0.369 and
/// Cambria 0.381 — a per-face term with no OS/2 metric that fits it (Georgia alone lands on its OS/2
/// <c>ySuperscriptYOffset</c>, 0.269). A third is within 3px of every face at 96pt.</item>
/// <item><b>Subscript</b>: lowered by 35% of the font's descent — the reduced glyph's descent bottom
/// stays on the full-size descent bottom (1 − 0.65). Calibri 0.094 × size against a 0.269 descent,
/// Times 0.075 against 0.216, Segoe UI 0.088 against 0.251, all within a pixel.</item>
/// </list>
/// The engine measures the run at the reduced size (<c>CanonicalParagraphMeasurer.Flatten</c>) and
/// carries the shift on the placed run (<see cref="PlacedRun.BaselineShift"/>), which every painter
/// subtracts from the line baseline. Before this the runs were measured full-size, drawn at 58%, and
/// sat ON the baseline.
/// </summary>
static class VerticalRunPosition
{
    /// <summary>The reduced size as a fraction of the run size.</summary>
    internal const double ReducedScale = 0.65;

    /// <summary>The size a run draws and measures at.</summary>
    internal static double RenderSizePoints(RunProperties properties) =>
        properties.VerticalAlignment == VerticalRunAlignment.Baseline
            ? properties.FontSizePoints
            : properties.FontSizePoints * ReducedScale;

    /// <summary>
    /// How far above the line baseline the run's own baseline sits, in points — positive for a
    /// superscript, negative for a subscript, zero otherwise. <paramref name="descentPoints"/> is the
    /// full-size font descent.
    /// </summary>
    internal static float BaselineShiftPoints(RunProperties properties, double descentPoints) =>
        properties.VerticalAlignment switch
        {
            VerticalRunAlignment.Superscript => (float) (properties.FontSizePoints / 3),
            VerticalRunAlignment.Subscript => (float) (-(1 - ReducedScale) * descentPoints),
            _ => 0f
        };
}
