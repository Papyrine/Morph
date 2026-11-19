using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// Where Word puts a header or footer float anchored to a margin strip (<c>topMargin</c> / <c>bottomMargin</c>,
/// <see cref="MarginStrip"/>) or to a paragraph of the band. Word-probed on <c>_probe_bandmargin</c>
/// (2026-11-19): a Letter page with 72pt margins, bands 36pt from the edge holding paragraphs on exact 20pt
/// lines, 100 x 40pt rectangles. Every expected position here is the one Word drew, read off its XPS.
/// </summary>
public class BandMarginStripTests
{
    const string namespaces =
        """xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape" """;

    [Test]
    [Arguments(1, 16f)]
    [Arguments(6, 58f)]
    public async Task Centred_in_the_top_margin_it_centres_between_the_page_top_and_the_body(int lines, float expected)
    {
        // One line ends at 56pt, inside the 72pt margin, so the strip is the margin; six end at 156pt and
        // push the body there, and the strip with it.
        var page = Layout(Band(lines, (0, Shape(Vertical("topMargin", align: "center")))), Band(1));

        await Assert.That(Shapes(page).Single().Y).IsEqualTo(expected).Within(0.01f);
    }

    [Test]
    [Arguments(1, 736f)]
    [Arguments(6, 694f)]
    public async Task Centred_in_the_bottom_margin_it_centres_between_the_footer_and_the_page_foot(int lines, float expected)
    {
        // One line starts at 736pt, inside the 72pt margin; six start at 636pt, and the strip with them.
        var page = Layout(Band(1), Band(lines, (0, Shape(Vertical("bottomMargin", align: "center")))));

        await Assert.That(Shapes(page).Single().Y).IsEqualTo(expected).Within(0.01f);
    }

    [Test]
    public async Task Aligned_to_a_strips_inner_edge_it_meets_the_body_or_the_footer()
    {
        var page = Layout(
            Band(6, (0, Shape(Vertical("topMargin", align: "bottom")))),
            Band(6, (0, Shape(Vertical("bottomMargin", align: "top")))));
        var shapes = Shapes(page);

        await Assert.That(shapes[0].Y + shapes[0].Height).IsEqualTo(156f).Within(0.01f);
        await Assert.That(shapes[1].Y).IsEqualTo(636f).Within(0.01f);
    }

    [Test]
    public async Task An_offset_in_a_strip_is_from_the_strips_top()
    {
        // The bottom strip's top is the six-line footer's, 636pt, not the margin's 720pt.
        var page = Layout(
            Band(6, (0, Shape(Vertical("topMargin", offset: 10)))),
            Band(6, (0, Shape(Vertical("bottomMargin", offset: 10)))));
        var shapes = Shapes(page);

        await Assert.That(shapes[0].Y).IsEqualTo(10f).Within(0.01f);
        await Assert.That(shapes[1].Y).IsEqualTo(646f).Within(0.01f);
    }

    [Test]
    public async Task Anchored_to_a_band_paragraph_it_stands_at_that_paragraphs_top()
    {
        // The third of six 20pt lines: 76pt in the header from 36pt, 676pt in the footer, which Word lays out
        // upwards from 36pt above the page's foot. The footer's floats stood at its foot before.
        var page = Layout(
            Band(6, (2, Shape(Vertical("paragraph", offset: 0)))),
            Band(6, (2, Shape(Vertical("paragraph", offset: 0)))));
        var shapes = Shapes(page);

        await Assert.That(shapes[0].Y).IsEqualTo(76f).Within(0.01f);
        await Assert.That(shapes[1].Y).IsEqualTo(676f).Within(0.01f);
    }

    [Test]
    public async Task A_box_that_fits_its_text_is_centred_by_the_height_its_text_gives_it()
    {
        // Declared 20pt, two 20pt lines and 3.6pt insets make it 47.2pt, centred on 78pt in the six-line
        // header's 156pt strip: Word's box stood at 54.4pt. Its outline is still drawn at the declared extent.
        var page = Layout(Band(6, (0, TextBox(Vertical("topMargin", align: "center")))), Band(1));

        await Assert.That(Shapes(page).Single().Y).IsEqualTo(54.4f).Within(0.01f);
    }

    static string Vertical(string relativeFrom, string? align = null, double offset = 0)
    {
        var position = align == null
            ? $"<wp:posOffset>{(long)(offset * 12700)}</wp:posOffset>"
            : $"<wp:align>{align}</wp:align>";
        return $"""<wp:positionV relativeFrom="{relativeFrom}">{position}</wp:positionV>""";
    }

    // A 100 x 40pt red rectangle at the left margin.
    static string Shape(string vertical) =>
        Drawing(vertical, 40, """<wps:cNvSpPr/>""", """<a:solidFill><a:srgbClr val="FF0000"/></a:solidFill><a:ln><a:noFill/></a:ln>""", "<wps:bodyPr/>");

    // A 100pt-wide text box declared 20pt tall that resizes to its two 20pt lines, outlined.
    static string TextBox(string vertical) =>
        Drawing(
            vertical,
            20,
            """<wps:cNvSpPr txBox="1"/>""",
            """<a:noFill/><a:ln w="12700"><a:solidFill><a:srgbClr val="000000"/></a:solidFill></a:ln>""",
            $"""<wps:txbx><w:txbxContent>{Paragraph("BOX1")}{Paragraph("BOX2")}</w:txbxContent></wps:txbx><wps:bodyPr lIns="91440" tIns="45720" rIns="91440" bIns="45720"><a:spAutoFit/></wps:bodyPr>""");

    static string Drawing(string vertical, int height, string nonVisual, string look, string body) =>
        $"""
         <w:r><w:drawing><wp:anchor distT="0" distB="0" distL="0" distR="0" simplePos="0" relativeHeight="251659265" behindDoc="0" locked="0" layoutInCell="1" allowOverlap="1">
         <wp:simplePos x="0" y="0"/><wp:positionH relativeFrom="margin"><wp:posOffset>0</wp:posOffset></wp:positionH>{vertical}
         <wp:extent cx="1270000" cy="{height * 12700}"/><wp:effectExtent l="0" t="0" r="0" b="0"/><wp:wrapNone/><wp:docPr id="1" name="Shape 1"/><wp:cNvGraphicFramePr/>
         <a:graphic><a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"><wps:wsp>{nonVisual}
         <wps:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="1270000" cy="{height * 12700}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom>{look}</wps:spPr>{body}
         </wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r>
         """;

    // Arial 12pt on an exact 20pt line, no spacing.
    static string Paragraph(string text, string drawing = "") =>
        $"""<w:p><w:pPr><w:spacing w:before="0" w:after="0" w:line="400" w:lineRule="exact"/></w:pPr>{drawing}<w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="24"/></w:rPr><w:t>{text}</w:t></w:r></w:p>""";

    static string Band(int lines, params (int Line, string Drawing)[] drawings) =>
        string.Concat(
            Enumerable.Range(0, lines)
                .Select(line => Paragraph($"L{line + 1}", string.Concat(drawings.Where(_ => _.Line == line).Select(_ => _.Drawing)))));

    static LaidOutPage Layout(string header, string footer)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            var headerPart = main.AddNewPart<HeaderPart>();
            var footerPart = main.AddNewPart<FooterPart>();
            Write(headerPart, $"<w:hdr {namespaces}>{header}</w:hdr>");
            Write(footerPart, $"<w:ftr {namespaces}>{footer}</w:ftr>");
            Write(
                main,
                $"""
                 <w:document {namespaces}><w:body>{Paragraph("BODY")}
                 <w:sectPr><w:headerReference w:type="default" r:id="{main.GetIdOfPart(headerPart)}"/><w:footerReference w:type="default" r:id="{main.GetIdOfPart(footerPart)}"/>
                 <w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>
                 </w:body></w:document>
                 """);
        }

        stream.Position = 0;
        var document = new DocumentParser().Parse(stream);
        return new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility)
            .Layout(
                document.Elements, document.PageSettings, document.Header, document.Footer,
                document.FirstPageHeader, document.FirstPageFooter, document.EvenPageHeader, document.EvenPageFooter,
                DocumentNotes.From(document))
            .Pages[0];
    }

    static void Write(OpenXmlPart part, string xml)
    {
        using var writer = new StreamWriter(part.GetStream(FileMode.Create));
        writer.Write(xml);
    }

    // The band's shapes, top to bottom.
    static List<PlacedShape> Shapes(LaidOutPage page) =>
        page.Items.OfType<PlacedShape>().OrderBy(_ => _.Y).ToList();
}
