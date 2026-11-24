using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// Lays out a document built from WordprocessingML fragments, the way the 2026-11-25 probes
/// (<c>_probe_blanks</c>, <c>_probe_colbreak</c>, <c>_probe_hdrspace</c>, <c>_probe_ftrstep</c>) were
/// built: 12pt Times New Roman on single lines with no spacing, on a Letter page with 1in margins and the
/// header and footer 0.5in from the edge, every face, size and spacing named so nothing comes from
/// Word's built-in Normal. Word's baselines on those probes are what the tests here hold Morph to.
/// </summary>
static class ProbeLayout
{
    const string namespaces =
        """xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" """;

    public static string Run(string text, int halfPoints = 24) =>
        $"""<w:r>{Properties(halfPoints)}<w:t xml:space="preserve">{text}</w:t></w:r>""";

    public static string Tab(int halfPoints = 24) =>
        $"<w:r>{Properties(halfPoints)}<w:tab/></w:r>";

    public static string Break(string type) =>
        $"""<w:r>{Properties(24)}<w:br w:type="{type}"/></w:r>""";

    public static string Paragraph(string runs, int afterPoints = 0, string properties = "", int markHalfPoints = 24) =>
        $"""<w:p><w:pPr>{properties}<w:spacing w:before="0" w:after="{afterPoints * 20}" w:line="240" w:lineRule="auto"/><w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:sz w:val="{markHalfPoints}"/></w:rPr></w:pPr>{runs}</w:p>""";

    public static string Text(string text) =>
        Paragraph(Run(text));

    public static string Lines(string label, int count) =>
        string.Concat(Enumerable.Range(1, count).Select(_ => Text($"{label} {_}")));

    public static string Section(string type = "nextPage", int bottomTwips = 1440, bool bands = false) =>
        $"""<w:sectPr>{(bands ? """<w:headerReference w:type="default" r:id="header"/><w:footerReference w:type="default" r:id="footer"/>""" : "")}<w:type w:val="{type}"/><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="{bottomTwips}" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/><w:cols w:space="720"/></w:sectPr>""";

    // A section break inside the body: an empty paragraph carrying the section's properties.
    public static string SectionBreak() =>
        $"<w:p><w:pPr>{Section()}</w:pPr></w:p>";

    public static string Table(string cellParagraphs) =>
        $"""<w:tbl><w:tblPr><w:tblW w:w="0" w:type="auto"/></w:tblPr><w:tblGrid><w:gridCol w:w="4000"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w="4000" w:type="dxa"/></w:tcPr>{cellParagraphs}</w:tc></w:tr></w:tbl>""";

    /// <summary>
    /// The laid-out pages of a body ending in <paramref name="section"/>, with a header and footer when
    /// given (the section must then reference them: <c>Section(bands: true)</c>).
    /// </summary>
    public static LaidOutDocument Layout(string body, string section, string? header = null, string? footer = null)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            if (header != null)
            {
                Write(main.AddNewPart<HeaderPart>("header"), $"<w:hdr {namespaces}>{header}</w:hdr>");
            }

            if (footer != null)
            {
                Write(main.AddNewPart<FooterPart>("footer"), $"<w:ftr {namespaces}>{footer}</w:ftr>");
            }

            Write(main, $"<w:document {namespaces}><w:body>{body}{section}</w:body></w:document>");
        }

        stream.Position = 0;
        var document = new DocumentParser().Parse(stream);
        return new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility)
            .Layout(
                document.Elements, document.PageSettings, document.Header, document.Footer,
                document.FirstPageHeader, document.FirstPageFooter, document.EvenPageHeader, document.EvenPageFooter);
    }

    /// <summary>Every placed line of a page with its text, in cells too, top to bottom.</summary>
    public static List<(float Baseline, string Text)> Lines(LaidOutPage page)
    {
        var lines = new List<(float, string)>();
        Collect(page.Items, lines);
        return lines.OrderBy(_ => _.Item1).ToList();
    }

    static void Collect(IReadOnlyList<PlacedItem> items, List<(float, string)> lines)
    {
        foreach (var item in items)
        {
            if (item is PlacedLine line)
            {
                lines.Add((line.Baseline, string.Concat(line.Runs.Select(_ => _.Text))));
            }
            else if (item is PlacedTableRow row)
            {
                foreach (var cell in row.Cells)
                {
                    Collect(cell.Content, lines);
                }
            }
        }
    }

    static string Properties(int halfPoints) =>
        $"""<w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman" w:cs="Times New Roman"/><w:sz w:val="{halfPoints}"/><w:szCs w:val="{halfPoints}"/></w:rPr>""";

    static void Write(OpenXmlPart part, string xml)
    {
        using var writer = new StreamWriter(part.GetStream(FileMode.Create));
        writer.Write(xml);
    }
}
