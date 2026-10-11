using DocumentFormat.OpenXml.Drawing.Wordprocessing;

/// <summary>
/// <c>wp14:sizeRelH</c> / <c>wp14:sizeRelV</c>: an anchor sized as a fraction of the page or of a
/// margin. Word takes the size from the page the object lands on, and the <c>wp:extent</c> beside it
/// is only what that came to when the file was saved.
///
/// Word-probed 2026-10-11 (<c>_probe_pctsize2</c>): rectangles each declared 72x36pt on a Letter page
/// with margins left 90, right 54, top 108 and bottom 72, one relative size apiece, measured off
/// Word's page. The cases below are the probe's.
/// </summary>
public class RelativeSizeTests
{
    const string wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    const string wp14 = "http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing";

    static readonly PageSettings page = new()
    {
        WidthPoints = 612,
        HeightPoints = 792,
        MarginLeft = 90,
        MarginRight = 54,
        MarginTop = 108,
        MarginBottom = 72
    };

    static (double Width, double Height) Fold(string relativeSize)
    {
        Anchor anchor =
        [
            with(
                $"""
                 <wp:anchor xmlns:wp="{wp}" xmlns:wp14="{wp14}" behindDoc="1" simplePos="0" relativeHeight="0" allowOverlap="1" layoutInCell="1" distT="0" distB="0" distL="0" distR="0">
                   <wp:extent cx="914400" cy="457200"/>
                   {relativeSize}
                 </wp:anchor>
                 """)
        ];
        // The fold walks a part's content for the anchors in it.
        new DocumentFormat.OpenXml.Wordprocessing.Drawing(anchor).FoldRelativeSizes(page);
        return (anchor.Extent!.Cx!.Value / 12700.0, anchor.Extent.Cy!.Value / 12700.0);
    }

    static string Horizontal(string relativeFrom, int thousandths) =>
        $"""<wp14:sizeRelH relativeFrom="{relativeFrom}"><wp14:pctWidth>{thousandths}</wp14:pctWidth></wp14:sizeRelH>""";

    static string Vertical(string relativeFrom, int thousandths) =>
        $"""<wp14:sizeRelV relativeFrom="{relativeFrom}"><wp14:pctHeight>{thousandths}</wp14:pctHeight></wp14:sizeRelV>""";

    [Test]
    [Arguments("page", 50000, 306.0)]
    [Arguments("margin", 50000, 234.0)]
    [Arguments("leftMargin", 100000, 90.0)]
    [Arguments("rightMargin", 100000, 54.0)]
    // On the first page Word measured the inside margin as the right one and the outside as the left.
    [Arguments("insideMargin", 100000, 54.0)]
    [Arguments("outsideMargin", 100000, 90.0)]
    public async Task A_relative_width_replaces_the_extent(string relativeFrom, int thousandths, double expected)
    {
        var (width, height) = Fold(Horizontal(relativeFrom, thousandths));

        await Assert.That(width).IsEqualTo(expected).Within(0.01);
        await Assert.That(height).IsEqualTo(36);
    }

    [Test]
    [Arguments("page", 10000, 79.2)]
    [Arguments("margin", 10000, 61.2)]
    [Arguments("topMargin", 50000, 54.0)]
    [Arguments("bottomMargin", 50000, 36.0)]
    // Vertically both came to half the TOP margin.
    [Arguments("insideMargin", 50000, 54.0)]
    [Arguments("outsideMargin", 50000, 54.0)]
    public async Task A_relative_height_replaces_the_extent(string relativeFrom, int thousandths, double expected)
    {
        var (width, height) = Fold(Vertical(relativeFrom, thousandths));

        await Assert.That(height).IsEqualTo(expected).Within(0.01);
        await Assert.That(width).IsEqualTo(72);
    }

    // Word writes <wp14:pctWidth>0</wp14:pctWidth> for an object that is not sized relatively.
    [Test]
    public async Task A_zero_percentage_leaves_the_extent()
    {
        var (width, height) = Fold(Horizontal("page", 0) + Vertical("page", 0));

        await Assert.That(width).IsEqualTo(72);
        await Assert.That(height).IsEqualTo(36);
    }

    // cover-letters/09's sidebar group is declared 257.6 x 792pt - 42% by 100% of the Letter page it
    // was drawn for - and stands on an A4 page, which Word fills to the foot.
    [Test]
    public async Task A_full_height_group_reaches_the_foot_of_a_taller_page()
    {
        var path = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "cover-letters", "09", "input.docx");
        using var stream = File.OpenRead(path);
        var document = new DocumentParser().Parse(stream);

        var shapes = document.Elements.OfType<FloatingShapeElement>().ToList();
        var bottom = shapes.Max(_ => _.VerticalPositionPoints + _.HeightPoints);
        var right = shapes.Max(_ => _.HorizontalPositionPoints + _.WidthPoints);
        var top = shapes.Min(_ => _.VerticalPositionPoints);

        await Assert.That(bottom - top).IsEqualTo(document.PageSettings.HeightPoints).Within(0.5);
        await Assert.That(right).IsEqualTo(document.PageSettings.WidthPoints * 0.42).Within(0.5);
    }
}
