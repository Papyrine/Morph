/// <summary>
/// Spec tests for Excel's column-width unit (ECMA-376 §18.3.1.13), probed at A4 against printed
/// gridlines across six faces. Two properties are pinned because each was wrong once:
///
/// The advance is EXACT, never rounded to whole pixels — Excel's fitted unit is 7.519 for
/// Calibri 11 against the face's 7.434, never an integer, and the earlier whole-pixel rounding
/// cost up to 5.8% per column, worst on the faces that round UP.
///
/// It is the widest of the digits 0-9, not the '0' — which only shows on a face with proportional
/// figures. Of the six probed, Corbel alone has a digit wider than its zero (7.691 against 7.534),
/// and taking the maximum moves its predicted width from 2.0% wide to 0.1%.
/// </summary>
public class ColumnWidthTests
{
    static double Measure(string family, double sizePoints, Func<string, string?>? fontFallback = null)
    {
        var fontDirectory = Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts");
        using var resolver = LayoutFonts.CreateResolver(fontDirectory, fontFallback);
        return SheetGridBuilder.MaxDigitWidth(LayoutFonts.ToDelegate(resolver), family, sizePoints);
    }

    [Test]
    public async Task Calibri11_IsTheExactAdvance_NotSeven()
    {
        var unit = Measure("Calibri", 11);

        await Assert.That(unit).IsEqualTo(7.434).Within(0.01);
    }

    [Test]
    public async Task Corbel11_UsesItsWidestDigit_NotTheZero()
    {
        // Corbel's zero advances 7.534px at 11pt; its widest digit 7.691. Excel's measured unit is
        // 7.685, so only the maximum matches.
        var unit = Measure("Corbel", 11);

        await Assert.That(unit).IsGreaterThan(7.6);
        await Assert.That(unit).IsEqualTo(7.691).Within(0.01);
    }

    /// <summary>
    /// The caller's substitution map decides the unit for a family the directory cannot serve — the
    /// grid has to be measured off the face the painter will draw, and the map is what names it.
    ///
    /// SpreadsheetParser dropped the map for a while, and the miss was invisible in both the corpus
    /// (no scenario supplies one) and the Blazor app (its map names Aptos, which is also
    /// DefaultFont, so the two agreed by coincidence). It shows the moment they differ: without the
    /// map an unresolvable family measures Aptos at 7.835, with it Georgia at 9.002 — a 15% wider
    /// column, on every output format, since the parse is shared.
    /// </summary>
    [Test]
    public async Task SubstitutedFace_IsMeasuredRatherThanTheDefaultFont()
    {
        var substituted = Measure("No Such Face 9000", 11, _ => "Georgia");
        var unsubstituted = Measure("No Such Face 9000", 11);

        await Assert.That(substituted).IsEqualTo(9.002).Within(0.01);
        await Assert.That(unsubstituted).IsEqualTo(7.835).Within(0.01);
    }

    /// <summary>
    /// And only for a family that genuinely misses: a map is a last resort, not an override, so a
    /// face the directory holds is measured as itself. Arial is in the bundled directory at 8.157.
    /// </summary>
    [Test]
    public async Task ResolvableFace_IgnoresTheSubstitutionMap()
    {
        var unit = Measure("Arial", 11, _ => "Georgia");

        await Assert.That(unit).IsEqualTo(8.157).Within(0.01);
    }

    [Test]
    public async Task UnresolvableFace_FallsBackToCalibri()
    {
        // The real resolver substitutes a face for any name, so the fallback branch only fires when
        // resolution genuinely fails — modelled here with a delegate that returns nothing.
        var unit = SheetGridBuilder.MaxDigitWidth((_, _, _) => null, "No Such Face 9000", 11);

        await Assert.That(unit).IsEqualTo(7.434);
    }
}
