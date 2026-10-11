using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// Parse rules settled against Word's own pages 2026-10-11: what a floating drawing's anchor does to the
/// paragraph it stands in, a cell cleared of its table style's shading, an octagon's cut, and the width
/// of a thin outline.
/// </summary>
public class DrawingAndTableParseRulesTests
{
    const string wNs = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    static ParsedDocument Parse(string body, string styles = "")
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new($"<w:document {wNs}><w:body>{body}</w:body></w:document>");
            main.AddNewPart<StyleDefinitionsPart>().Styles = new($"<w:styles {wNs}>{styles}</w:styles>");
        }

        stream.Position = 0;
        return new DocumentParser().Parse(stream);
    }

    // brochures/06's second page opens on a 4pt paragraph holding a space, an anchored group and another
    // space. Word sets that as one line; closed at the anchor it was two paragraphs and two lines, and
    // the page stood 4.9pt low.
    [Test]
    public async Task Text_either_side_of_a_floating_drawing_is_one_paragraph()
    {
        await using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "brochures", "06", "input.docx"));
        var elements = new DocumentParser().Parse(stream).Elements;
        var pageBreak = elements.ToList().FindIndex(_ => _ is PageBreakElement);
        var afterBreak = elements.Skip(pageBreak + 1).TakeWhile(_ => _ is not TableElement).ToList();

        // The group's shapes, then the one paragraph that anchors them, with both of its spaces.
        var paragraphs = afterBreak.OfType<ParagraphElement>().ToList();
        await Assert.That(paragraphs.Count).IsEqualTo(1);
        await Assert.That(string.Concat(paragraphs[0].Runs.Select(_ => _.Text))).IsEqualTo("  ");
        await Assert.That(afterBreak.OfType<FloatingShapeElement>().Count()).IsGreaterThan(1);
        await Assert.That(afterBreak.OfType<FloatingShapeElement>().All(_ => ReferenceEquals(_.AnchorParagraph, paragraphs[0]))).IsTrue();
    }

    // page_vertical_alignment: a page-anchored picture (z 101) and a paragraph-anchored one (z 102) on
    // one paragraph. The page-anchored one waits for its page; it must still paint first.
    [Test]
    public async Task Floats_paint_in_the_order_the_document_declares_them()
    {
        await using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "page_vertical_alignment", "input.docx"));
        var document = new DocumentParser().Parse(stream);
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility).Layout(document.Elements, document.PageSettings);
        var pictures = laidOut.Pages[9].Items.OfType<PlacedImage>().ToList();

        await Assert.That(pictures.Count).IsEqualTo(2);
        await Assert.That(pictures[0].X).IsEqualTo(400f).Within(0.01f);
        await Assert.That(pictures[1].X).IsEqualTo(372f).Within(0.01f);
    }

    const string bandedStyle =
        """
        <w:style w:type="table" w:styleId="Banded"><w:name w:val="Banded"/>
        <w:tblPr><w:tblStyleRowBandSize w:val="1"/></w:tblPr>
        <w:tblStylePr w:type="firstRow"><w:tcPr><w:shd w:val="clear" w:color="auto" w:fill="000000"/></w:tcPr></w:tblStylePr>
        <w:tblStylePr w:type="band1Horz"><w:tcPr><w:shd w:val="clear" w:color="auto" w:fill="D0D0D0"/></w:tcPr></w:tblStylePr>
        </w:style>
        """;

    // Word writes a row's conditional format both ways: the bit string and the named flag.
    static string Row(string firstCell, string cnfStyle, string flag) =>
        $"""<w:tr><w:trPr><w:cnfStyle w:val="{cnfStyle}" w:{flag}="1"/></w:trPr><w:tc><w:tcPr><w:tcW w:w="2000" w:type="dxa"/>{firstCell}</w:tcPr><w:p/></w:tc><w:tc><w:tcPr><w:tcW w:w="2000" w:type="dxa"/></w:tcPr><w:p/></w:tc></w:tr>""";

    // business-plans/15 stands two tables side by side as one; the spacer column between them is
    // cleared with Shading > No Color, and Word leaves it bare of the header fill and the banding.
    [Test]
    public async Task A_cell_shaded_with_no_colour_is_cleared_of_its_table_styles_shading()
    {
        const string cleared = """<w:shd w:val="clear" w:color="auto" w:fill="auto"/>""";
        var table = (TableElement) Parse(
            $"""<w:tbl><w:tblPr><w:tblStyle w:val="Banded"/><w:tblLook w:val="0420" w:firstRow="1" w:noHBand="0"/></w:tblPr><w:tblGrid><w:gridCol w:w="2000"/><w:gridCol w:w="2000"/></w:tblGrid>{Row(cleared, "100000000000", "firstRow")}{Row(cleared, "000000100000", "oddHBand")}</w:tbl><w:p/>""",
            bandedStyle).Elements[0];

        await Assert.That(table.Rows[0].Cells[0].Properties.BackgroundColorHex).IsNull();
        await Assert.That(table.Rows[0].Cells[1].Properties.BackgroundColorHex).IsEqualTo("000000");
        await Assert.That(table.Rows[1].Cells[0].Properties.BackgroundColorHex).IsNull();
        await Assert.That(table.Rows[1].Cells[1].Properties.BackgroundColorHex).IsEqualTo("D0D0D0");
    }

    // menus/07's frame declares a cut of 11057; read under the wrong guide name it took the default 29289.
    [Test]
    public async Task An_octagon_is_cut_by_its_own_adjust_value()
    {
        var declared = new A.PresetGeometry(new A.AdjustValueList(new A.ShapeGuide {Name = "adj", Formula = "val 11057"})) {Preset = A.ShapeTypeValues.Octagon};
        var contour = PresetShapeGeometry.TryBuild(declared, 200, 100)![0];
        await Assert.That(contour[0].X).IsEqualTo(0.11057 * 100 / 200).Within(0.0001);
        await Assert.That(contour[^1].Y).IsEqualTo(0.11057).Within(0.0001);

        var plain = new A.PresetGeometry(new A.AdjustValueList()) {Preset = A.ShapeTypeValues.Octagon};
        await Assert.That(PresetShapeGeometry.TryBuild(plain, 200, 100)![0][^1].Y).IsEqualTo(0.29289).Within(0.0001);
    }

    // A quarter-point accent rule was drawn at half a point, twice Word's ink.
    [Test]
    public async Task An_outline_is_stroked_at_the_width_it_declares_however_thin()
    {
        await Assert.That(ShapeStroke.WidthPoints(0.25)).IsEqualTo(0.25);
        await Assert.That(ShapeStroke.WidthPoints(0.34)).IsEqualTo(0.34);
        await Assert.That(ShapeStroke.WidthPoints(3)).IsEqualTo(3);
        await Assert.That(ShapeStroke.WidthPoints(0)).IsEqualTo(0.5);
        await Assert.That(ShapeStroke.WidthPoints(null)).IsEqualTo(0.75);
    }
}
