/// <summary>
/// Covers the OOXML rule that a direct `<a:noFill/>` in `a:spPr` takes precedence
/// over any `<a:fillRef>` in the shape's `wps:style`. Previously the parser would
/// fall back to the fillRef when `a:solidFill` was absent, erroneously painting a
/// large coloured rectangle over the page background.
/// </summary>
public class ShapeNoFillTests
{
    /// <summary>
    /// cover-letters/03's header anchors a wgp group with two rectangles:
    ///   - Rectangle 1 (full page): explicit `<a:noFill/>` plus a `wps:style` fillRef=accent1
    ///   - Rectangle 2 (right-side bar): solidFill accent5 lumMod 75000 → #463E6A
    /// The page itself is filled by `<w:background w:color="2E2946"/>` (accent5 shade 7F).
    /// So the parsed header must emit only ONE FloatingShapeElement (the right-side bar),
    /// not two.
    /// </summary>
    [Test]
    public async Task NoFillInSpPrSuppressesFillRefFromWpsStyle()
    {
        var parser = new DocumentParser();
        using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "cover-letters", "03", "input.docx"));
        var doc = parser.Parse(stream);

        await Assert.That(doc.PageSettings.BackgroundColorHex).IsEqualTo("2E2946");

        var headerShapes = doc.Header!.Elements.OfType<FloatingShapeElement>().ToList();
        await Assert.That(headerShapes.Count).IsEqualTo(1);

        var rightBar = headerShapes[0];
        // accent5 (5D538D) with lumMod 75000 → ~#463E6A
        await Assert.That(rightBar.FillColorHex).IsEqualTo("463E6A");
        // Positioned on the right half of the page
        await Assert.That(rightBar.HorizontalPositionPoints).IsGreaterThan(400);
        await Assert.That(rightBar.WidthPoints).IsGreaterThan(100);
    }
}
