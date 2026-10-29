using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// A floating DrawingML text box in a header or footer lays out like a body one (issue #194): the band
/// float pass placed images and shapes but not the floating WordArt such a box parses as, so it was
/// dropped silently.
/// </summary>
public class BandTextBoxLayoutTests
{
    const string namespaces =
        """xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" """;

    static string TextBox(bool behindDoc) =>
        $"""
         <w:p><w:r><mc:AlternateContent><mc:Choice Requires="wps"><w:drawing>
         <wp:anchor distT="0" distB="0" distL="114300" distR="114300" simplePos="0" relativeHeight="251658241" behindDoc="{(behindDoc ? 1 : 0)}" locked="0" layoutInCell="1" allowOverlap="1">
         <wp:simplePos x="0" y="0"/>
         <wp:positionH relativeFrom="page"><wp:posOffset>6643396</wp:posOffset></wp:positionH>
         <wp:positionV relativeFrom="page"><wp:posOffset>233265</wp:posOffset></wp:positionV>
         <wp:extent cx="1020406" cy="164465"/><wp:effectExtent l="0" t="0" r="8890" b="635"/>
         <wp:wrapNone/><wp:docPr id="443" name="docshape2"/>
         <a:graphic><a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"><wps:wsp>
         <wps:cNvSpPr txBox="1"/>
         <wps:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="1020406" cy="164465"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/><a:ln><a:noFill/></a:ln></wps:spPr>
         <wps:txbx><w:txbxContent><w:p><w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="16"/></w:rPr><w:t>BAND TEXT BOX</w:t></w:r></w:p></w:txbxContent></wps:txbx>
         <wps:bodyPr rot="0" vert="horz" wrap="square" lIns="0" tIns="0" rIns="0" bIns="0" anchor="t"><a:noAutofit/></wps:bodyPr>
         </wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></mc:Choice></mc:AlternateContent></w:r></w:p>
         """;

    static ParsedDocument Build(bool inFooter, bool behindDoc)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            OpenXmlPart band = inFooter ? main.AddNewPart<FooterPart>() : main.AddNewPart<HeaderPart>();
            var bandId = main.GetIdOfPart(band);
            var root = inFooter ? "ftr" : "hdr";
            var reference = inFooter ? "footerReference" : "headerReference";
            Write(band, $"<w:{root} {namespaces}>{TextBox(behindDoc)}</w:{root}>");
            Write(
                main,
                $"""
                 <w:document {namespaces}><w:body><w:p><w:r><w:t>Body text.</w:t></w:r></w:p>
                 <w:sectPr><w:{reference} w:type="default" r:id="{bandId}"/><w:pgSz w:w="12240" w:h="15840"/>
                 <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>
                 </w:body></w:document>
                 """);
        }

        stream.Position = 0;
        return new DocumentParser().Parse(stream);
    }

    static void Write(OpenXmlPart part, string xml)
    {
        using var writer = new StreamWriter(part.GetStream(FileMode.Create));
        writer.Write(xml);
    }

    [Test]
    [Arguments(false, true)]
    [Arguments(false, false)]
    [Arguments(true, true)]
    [Arguments(true, false)]
    public async Task Band_text_box_is_placed(bool inFooter, bool behindDoc)
    {
        var document = Build(inFooter, behindDoc);
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility)
            .Layout(
                document.Elements, document.PageSettings, document.Header, document.Footer,
                document.FirstPageHeader, document.FirstPageFooter, document.EvenPageHeader, document.EvenPageFooter,
                DocumentNotes.From(document));

        var line = laidOut.Pages[0].Items.OfType<PlacedLine>()
            .SingleOrDefault(_ => _.Paragraph.Runs.Any(run => run.Text.Contains("BAND TEXT BOX")));
        await Assert.That(line).IsNotNull();
        // Page-anchored box: 523.1pt across, 18.4pt down, 80.3pt x 13pt.
        await Assert.That(line!.X).IsBetween(523f, 603.5f);
        await Assert.That(line.Y).IsBetween(18f, 31.5f);
    }
}
