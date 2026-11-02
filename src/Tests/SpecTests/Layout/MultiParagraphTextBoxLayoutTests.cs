using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// A floating DrawingML text box holding more than one paragraph lays each paragraph out on its own lines,
/// at its own indent, wrapping in what its indent leaves of the box (issue #199). It went through the
/// WordArt dispatch, which drew all of its text as one line from the box's left edge: "FIRST PARASECOND
/// PARA".
/// </summary>
public class MultiParagraphTextBoxLayoutTests
{
    const string namespaces =
        """xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" """;

    // The issue's box: page-anchored at 468pt across and 18pt down, 91.8pt wide, zero insets, its first
    // paragraph indented 50.75pt at 8pt and its second 1pt at 6pt.
    static string TextBox(long heightEmu, string insets) =>
        $"""
         <w:p><w:r><mc:AlternateContent><mc:Choice Requires="wps"><w:drawing>
         <wp:anchor distT="0" distB="0" distL="0" distR="0" simplePos="0" relativeHeight="1" behindDoc="0" locked="0" layoutInCell="1" allowOverlap="1">
         <wp:simplePos x="0" y="0"/>
         <wp:positionH relativeFrom="page"><wp:posOffset>5943600</wp:posOffset></wp:positionH>
         <wp:positionV relativeFrom="page"><wp:posOffset>228600</wp:posOffset></wp:positionV>
         <wp:extent cx="1165860" cy="{heightEmu}"/><wp:effectExtent l="0" t="0" r="0" b="0"/><wp:wrapNone/>
         <wp:docPr id="1" name="TextBox1"/><wp:cNvGraphicFramePr/>
         <a:graphic><a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"><wps:wsp>
         <wps:cNvSpPr txBox="1"/>
         <wps:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="1165860" cy="{heightEmu}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/><a:ln><a:noFill/></a:ln></wps:spPr>
         <wps:txbx><w:txbxContent>
         <w:p><w:pPr><w:ind w:left="1015"/></w:pPr><w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="16"/></w:rPr><w:t>FIRST PARA</w:t></w:r></w:p>
         <w:p><w:pPr><w:ind w:left="20"/></w:pPr><w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="12"/></w:rPr><w:t>SECOND PARA</w:t></w:r></w:p>
         </w:txbxContent></wps:txbx>
         <wps:bodyPr rot="0" vert="horz" wrap="square" {insets} anchor="t" anchorCtr="0" upright="1"><a:noAutofit/></wps:bodyPr>
         </wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></mc:Choice></mc:AlternateContent></w:r></w:p>
         """;

    const string zeroInsets = """lIns="0" tIns="0" rIns="0" bIns="0" """;

    static LaidOutPage Layout(bool inHeader, long heightEmu = 243840, string insets = zeroInsets)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            var header = main.AddNewPart<HeaderPart>();
            var headerId = main.GetIdOfPart(header);
            var box = TextBox(heightEmu, insets);
            Write(header, $"<w:hdr {namespaces}>{(inHeader ? box : "<w:p/>")}</w:hdr>");
            Write(
                main,
                $"""
                 <w:document {namespaces}><w:body><w:p><w:r><w:t>Body text.</w:t></w:r></w:p>{(inHeader ? "" : box)}
                 <w:sectPr><w:headerReference w:type="default" r:id="{headerId}"/><w:pgSz w:w="12240" w:h="15840"/>
                 <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>
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

    static List<PlacedLine> Lines(LaidOutPage page, string text) =>
        [.. page.Items.OfType<PlacedLine>().Where(_ => _.Paragraph.Runs.Any(run => run.Text.Contains(text)))];

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Each_paragraph_takes_its_own_indent_and_wraps_in_what_it_leaves(bool inHeader)
    {
        var page = Layout(inHeader);

        // 50.75pt of indent leaves ~41pt of the box, so the first paragraph wraps: FIRST over PARA.
        var first = Lines(page, "FIRST");
        await Assert.That(first.Count).IsEqualTo(2);
        await Assert.That(first[0].X).IsEqualTo(518.75f).Within(0.5f);
        await Assert.That(first[1].X).IsEqualTo(518.75f).Within(0.5f);
        await Assert.That(first[0].Y).IsBetween(17.5f, 19f);
        await Assert.That(first[1].Y).IsGreaterThan(first[0].Y);
        await Assert.That(page.Items.OfType<PlacedLine>().Any(_ => _.Paragraph.Runs.Any(run => run.Text.Contains("PARASECOND")))).IsFalse();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task The_second_paragraph_starts_a_line_of_its_own(bool inHeader)
    {
        // 36pt tall, so the second paragraph is inside the box rather than past its foot.
        var page = Layout(inHeader, heightEmu: 457200);

        var first = Lines(page, "FIRST");
        var second = Lines(page, "SECOND");
        await Assert.That(second.Count).IsEqualTo(1);
        await Assert.That(second[0].X).IsEqualTo(469f).Within(0.5f);
        await Assert.That(second[0].Y).IsGreaterThan(first[^1].Y);
    }

    [Test]
    public async Task The_box_insets_move_the_text_in()
    {
        // 9pt left and 4.5pt top.
        var page = Layout(inHeader: false, heightEmu: 457200, insets: """lIns="114300" tIns="57150" rIns="0" bIns="0" """);

        var first = Lines(page, "FIRST");
        var second = Lines(page, "SECOND");
        await Assert.That(first[0].X).IsEqualTo(527.75f).Within(0.5f);
        await Assert.That(first[0].Y).IsBetween(22f, 23.5f);
        await Assert.That(second[0].X).IsEqualTo(478f).Within(0.5f);
    }
}
