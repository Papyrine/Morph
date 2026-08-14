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
    static double Measure(string family, double sizePoints)
    {
        var fontDirectory = Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts");
        using var resolver = LayoutFonts.CreateResolver(fontDirectory, null);
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

    [Test]
    public async Task UnresolvableFace_FallsBackToCalibri()
    {
        // The real resolver substitutes a face for any name, so the fallback branch only fires when
        // resolution genuinely fails — modelled here with a delegate that returns nothing.
        var unit = SheetGridBuilder.MaxDigitWidth((_, _, _) => null, "No Such Face 9000", 11);

        await Assert.That(unit).IsEqualTo(7.434);
    }
}
