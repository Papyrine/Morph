/// <summary>
/// Tests for w:pgBorders parsing (MS-DOCX § 17.6.10).
/// </summary>
public class PageBordersTests
{
    [Test]
    public async Task PageBorders_Defaults_AreNone()
    {
        var borders = new PageBorders();

        await Assert.That(borders.Top.IsVisible).IsFalse();
        await Assert.That(borders.Right.IsVisible).IsFalse();
        await Assert.That(borders.Bottom.IsVisible).IsFalse();
        await Assert.That(borders.Left.IsVisible).IsFalse();
        await Assert.That(borders.HasAnyBorder).IsFalse();
        await Assert.That(borders.TopSpacePoints).IsEqualTo(24);
    }

    [Test]
    public async Task PageBorders_HasAnyBorder_TrueWhenAnyEdgeVisible()
    {
        var borders = new PageBorders { Top = BorderEdge.Default };
        await Assert.That(borders.HasAnyBorder).IsTrue();
    }

    [Test]
    public async Task DocumentParser_ParsesPageBorders()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "page_borders", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.PageSettings.PageBorders).IsNotNull();
        var borders = doc.PageSettings.PageBorders!;
        await Assert.That(borders.Top.IsVisible).IsTrue();
        await Assert.That(borders.Right.IsVisible).IsTrue();
        await Assert.That(borders.Bottom.IsVisible).IsTrue();
        await Assert.That(borders.Left.IsVisible).IsTrue();
        await Assert.That(borders.Top.WidthPoints).IsEqualTo(3); // sz=24 → 24/8 = 3pt
        await Assert.That(borders.Top.ColorHex).IsEqualTo("000000");
        await Assert.That(borders.TopSpacePoints).IsEqualTo(24);
    }

    [Test]
    public async Task DocumentParser_NoPageBorders_WhenAbsent()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "all_caps", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        await Assert.That(doc.PageSettings.PageBorders).IsNull();
    }
}
