using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// How a header's or footer's paragraphs stack, and how a tabbed line is centred or right-aligned.
/// Word-probed 2026-11-24: a Letter page with 72pt margins, bands 36pt from the edge, Arial 12pt on exact
/// 20pt lines. Every expected position here is the one Word drew, read off its XPS.
/// </summary>
public class BandStackingTests
{
    const string namespaces =
        """xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" """;

    const string rule = """<w:pBdr><w:bottom w:val="single" w:sz="36" w:space="3" w:color="0000FF"/></w:pBdr>""";

    static readonly string band = Paragraph("A", after: 6) + Paragraph("B", before: 12, after: 6) + Paragraph("C", before: 2, after: 12, extra: rule);

    [Test]
    public async Task A_headers_paragraphs_keep_the_larger_of_after_and_before()
    {
        var tops = Tops(Layout(band, Paragraph("F"), Paragraph("BODY")));

        await Assert.That(tops["B"] - tops["A"]).IsEqualTo(32f).Within(0.01f);
        await Assert.That(tops["C"] - tops["B"]).IsEqualTo(26f).Within(0.01f);
    }

    [Test]
    public async Task A_headers_bottom_rule_is_drawn_and_pushes_the_body()
    {
        var page = Layout(band, Paragraph("F"), Paragraph("BODY"));
        var tops = Tops(page);
        var box = page.Items.OfType<PlacedBorder>().Single();

        await Assert.That(box.Y + box.Height).IsEqualTo(117f).Within(0.01f);
        await Assert.That(tops["BODY"] - tops["C"]).IsEqualTo(39.5f).Within(0.01f);
    }

    [Test]
    public async Task A_footer_stacks_the_same_way_up_from_the_footer_distance()
    {
        var page = Layout(Paragraph("H"), band, Paragraph("BODY"));
        var tops = Tops(page);
        var box = page.Items.OfType<PlacedBorder>().Single();

        await Assert.That(tops["B"] - tops["A"]).IsEqualTo(32f).Within(0.01f);
        await Assert.That(tops["C"] - tops["B"]).IsEqualTo(26f).Within(0.01f);
        await Assert.That(box.Y + box.Height).IsEqualTo(739.5f).Within(0.01f);
    }

    [Test]
    public async Task A_centred_line_is_centred_with_its_tabs()
    {
        var lines = Lines(
            Layout(
                Paragraph("H"),
                Paragraph("F"),
                Paragraph("CL\tCM", extra: """<w:tabs><w:tab w:val="left" w:pos="2000"/></w:tabs><w:jc w:val="center"/>""") +
                Paragraph("CX\tCY", extra: """<w:tabs><w:tab w:val="right" w:pos="6000"/></w:tabs><w:jc w:val="center"/>""") +
                Paragraph("CA\tCB\tCC", extra: """<w:tabs><w:tab w:val="left" w:pos="3000"/><w:tab w:val="right" w:pos="9360"/></w:tabs><w:jc w:val="center"/>""")));

        await Assert.That(lines["CL"].Width).IsGreaterThan(100f);
        await Assert.That(lines["CL"].X + lines["CL"].Width / 2).IsEqualTo(306f).Within(0.01f);
        await Assert.That(lines["CX"].X).IsEqualTo(156f).Within(0.01f);
        await Assert.That(lines["CA"].X).IsEqualTo(72f).Within(0.01f);
    }

    [Test]
    public async Task A_right_aligned_line_ends_at_the_margin_with_its_tabs()
    {
        var lines = Lines(
            Layout(
                Paragraph("H"),
                Paragraph("F"),
                Paragraph("RL\tRM", extra: """<w:tabs><w:tab w:val="left" w:pos="2000"/></w:tabs><w:jc w:val="right"/>""")));

        await Assert.That(lines["RL"].Width).IsGreaterThan(100f);
        await Assert.That(lines["RL"].X + lines["RL"].Width).IsEqualTo(540f).Within(0.01f);
    }

    static string Paragraph(string text, int before = 0, int after = 0, string extra = "")
    {
        var runs = string.Join(
            "<w:r><w:tab/></w:r>",
            text.Split('\t').Select(_ => $"""<w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="24"/></w:rPr><w:t>{_}</w:t></w:r>"""));
        return $"""<w:p><w:pPr>{extra}<w:spacing w:before="{before * 20}" w:after="{after * 20}" w:line="400" w:lineRule="exact"/></w:pPr>{runs}</w:p>""";
    }

    static LaidOutPage Layout(string header, string footer, string body)
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
                 <w:document {namespaces}><w:body>{body}
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

    static Dictionary<string, PlacedLine> Lines(LaidOutPage page) =>
        page.Items.OfType<PlacedLine>().ToDictionary(_ => _.Paragraph.Runs.First(run => !string.IsNullOrEmpty(run.Text)).Text);

    static Dictionary<string, float> Tops(LaidOutPage page) =>
        Lines(page).ToDictionary(_ => _.Key, _ => _.Value.Baseline);
}
