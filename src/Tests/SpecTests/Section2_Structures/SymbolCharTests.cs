using Morph.PDFium;

/// <summary>
/// A <c>w:sym</c> — here Wingdings' checkboxes, F0FE (☑) and F06F (☐), as a generated brief writes a
/// ticked and an unticked option — is a character on the line like any other: measured, wrapped and
/// drawn by every backend. Where Wingdings is available it is drawn from Wingdings, through the font's
/// symbol <c>cmap</c>; where it is not, from its Unicode equivalent in the embedded Morph Bullets face.
/// Until these, the parser read past the element and the option's state vanished from the page.
/// </summary>
public class SymbolCharTests
{
    const int dpi = 72;

    // A directory holding Wingdings and nothing else: the text's Aptos comes from Morph.dll.
    static readonly string wingdingsDirectory = Path.Combine(ProjectFiles.ProjectDirectory, "SymbolFonts");

    // A directory without Wingdings, as on any host that does not have it.
    static readonly string fallbackDirectory = ProjectFonts.Directory;

    static string FontsFor(string fonts)
    {
        if (fonts == "wingdings")
        {
            return wingdingsDirectory;
        }

        return fallbackDirectory;
    }

    // The font name in the casing a generator wrote it in; font names are not case-sensitive.
    const string checkedBox = """<w:r><w:rPr><w:rFonts w:ascii="Aptos" w:hAnsi="Aptos"/><w:sz w:val="40"/></w:rPr><w:sym w:font="WingDings" w:char="F0FE"/></w:r>""";
    const string uncheckedBox = """<w:r><w:rPr><w:rFonts w:ascii="Aptos" w:hAnsi="Aptos"/><w:sz w:val="40"/></w:rPr><w:sym w:font="WingDings" w:char="F06F"/></w:r>""";

    static string Text(string text) =>
        $"""<w:r><w:rPr><w:rFonts w:ascii="Aptos" w:hAnsi="Aptos"/><w:sz w:val="40"/></w:rPr><w:t xml:space="preserve">{text}</w:t></w:r>""";

    static readonly string options = checkedBox + Text(" Yes  ") + uncheckedBox + Text(" No");

    [Test]
    public async Task The_parser_reads_a_sym_as_a_run_of_its_own()
    {
        var runs = Paragraph(options).Runs;

        await Assert.That(runs.Count).IsEqualTo(4);
        await Assert.That(runs[0].Symbol).IsEqualTo(new RunSymbol("WingDings", ''));
        await Assert.That(runs[0].Text).IsEqualTo("☑");
        await Assert.That(runs[0].Properties.FontFamily).IsEqualTo("WingDings");
        await Assert.That(runs[2].Symbol).IsEqualTo(new RunSymbol("WingDings", ''));
        await Assert.That(runs[2].Text).IsEqualTo("☐");
        await Assert.That(runs[1].Text).IsEqualTo(" Yes  ");
    }

    [Test]
    [Arguments('', '☐')]
    [Arguments('', '☑')]
    [Arguments('', '☒')]
    [Arguments('', '☐')]
    [Arguments('', '☒')]
    [Arguments('', '✓')]
    // The low byte alone names the same glyph.
    [Arguments('þ', '☑')]
    public async Task Wingdings_checkboxes_have_Unicode_equivalents(char code, char expected)
    {
        await Assert.That(SymbolFonts.ToUnicode("wingdings", code)).IsEqualTo(expected);
        await Assert.That(SymbolFonts.ToUnicode("Wingdings", code)).IsEqualTo(expected);
    }

    // The fallback has to be able to draw every checkbox it is handed, or the box would be measured
    // and drawn as the missing-glyph box after all.
    [Test]
    public async Task The_fallback_face_draws_every_checkbox()
    {
        var bullets = FontMetricsReader.Read(new MemoryStream(EmbeddedFonts.Bullets))!;
        foreach (var box in "☐☑☒✓")
        {
            await Assert.That(bullets.GlyphId(box)).IsNotEqualTo((ushort) 0);
        }
    }

    // Wingdings has no Unicode cmap at all: its glyphs are only reachable through the symbol subtable.
    [Test]
    public async Task The_metrics_reader_reads_a_symbol_cmap()
    {
        var wingdings = FontMetricsReader.Read(Path.Combine(wingdingsDirectory, "Wingdings_400.ttf"), 0)!;

        await Assert.That(wingdings.GlyphId(0xF0FE)).IsNotEqualTo((ushort) 0);
        await Assert.That(wingdings.GlyphId(0xF06F)).IsNotEqualTo((ushort) 0);
    }

    [Test]
    public async Task With_Wingdings_the_boxes_are_laid_out_in_Wingdings()
    {
        var runs = PlacedRuns(Document(options), wingdingsDirectory);

        var boxes = runs.Where(_ => _.Properties.FontFamily == "WingDings").ToList();
        await Assert.That(boxes.Select(_ => _.Text)).IsEquivalentTo(["", ""]);
        await AssertMeasuredLikeText(runs);
    }

    [Test]
    public async Task Without_Wingdings_the_boxes_are_laid_out_in_the_fallback()
    {
        var runs = PlacedRuns(Document(options), fallbackDirectory);

        var boxes = runs.Where(_ => _.Properties.FontFamily == SymbolFonts.Fallback).ToList();
        await Assert.That(boxes.Select(_ => _.Text)).IsEquivalentTo(["☑", "☐"]);
        await AssertMeasuredLikeText(runs);
    }

    // Each box takes its own width on the line, and the text after it starts past it.
    static async Task AssertMeasuredLikeText(List<PlacedRun> runs)
    {
        await Assert.That(runs.Count).IsEqualTo(4);
        for (var index = 0; index < runs.Count - 1; index++)
        {
            await Assert.That(runs[index].Width).IsGreaterThan(5);
            await Assert.That(runs[index + 1].X).IsGreaterThanOrEqualTo(runs[index].X + runs[index].Width - 0.01f);
        }
    }

    // A long line of options wraps between them, and a box is never lost to the wrap.
    [Test]
    [Arguments("wingdings")]
    [Arguments("fallback")]
    public async Task A_line_of_boxes_wraps_like_text(string fonts)
    {
        var body = string.Concat(Enumerable.Repeat(options + Text("  "), 6));
        var laidOut = Layout(Document(body), FontsFor(fonts));
        var lines = laidOut.Pages.SelectMany(_ => _.Items).OfType<PlacedLine>().ToList();
        var boxes = lines.SelectMany(_ => _.Runs).Count(_ => _.Text is "" or "" or "☑" or "☐");

        await Assert.That(lines.Count).IsGreaterThan(1);
        await Assert.That(boxes).IsEqualTo(12);
    }

    [Test]
    public async Task The_text_exporters_write_the_Unicode_boxes()
    {
        var markdown = MarkdownExporter.Export(Document(options));

        await Assert.That(markdown).Contains("☑ Yes");
        await Assert.That(markdown).Contains("☐ No");
    }

    // A box alone on the page is drawn: its ink stands about as tall as the 20pt text it sits in.
    [Test]
    [Arguments("skia", "wingdings")]
    [Arguments("skia", "fallback")]
    [Arguments("imagesharp", "wingdings")]
    [Arguments("imagesharp", "fallback")]
    [Arguments("pdf", "wingdings")]
    [Arguments("pdf", "fallback")]
    public async Task Each_backend_draws_the_boxes(string backend, string fonts)
    {
        foreach (var box in new[] { checkedBox, uncheckedBox })
        {
            var (width, height) = InkSize(Render(backend, Document(box), FontsFor(fonts)));

            await Assert.That(width).IsGreaterThan(10);
            await Assert.That(height).IsGreaterThan(10);
        }
    }

    [Test]
    [Arguments("skia", "wingdings")]
    [Arguments("skia", "fallback")]
    [Arguments("imagesharp", "wingdings")]
    [Arguments("imagesharp", "fallback")]
    [Arguments("pdf", "wingdings")]
    [Arguments("pdf", "fallback")]
    public Task Boxes_beside_text(string backend, string fonts) =>
        Verify(new Target("png", new MemoryStream(Render(backend, Document(options), FontsFor(fonts)))));

    static byte[] Render(string backend, ParsedDocument document, string fontDirectory)
    {
        if (backend == "pdf")
        {
            var pdf = PdfRenderer.Render(document, new() { FontDirectory = fontDirectory });
            using var pages = PdfiumDocument.Load(pdf);
            return pages.RenderPage(0, dpi);
        }

        var imageOptions = new ImageExportOptions { Dpi = dpi, FontDirectory = fontDirectory, DeterministicRendering = true };
        byte[]? page = null;
        void Keep(Action<Stream> write)
        {
            using var stream = new MemoryStream();
            write(stream);
            page ??= stream.ToArray();
        }

        if (backend == "skia")
        {
            SkiaDocumentConverter.RenderPagesCounted(document, imageOptions, Keep);
        }
        else
        {
            ImageSharpDocumentConverter.RenderPagesCounted(document, imageOptions, Keep);
        }

        return page!;
    }

    // The size of the box around the pixels darker than mid-grey.
    static (int Width, int Height) InkSize(byte[] png)
    {
        using var image = Image.Load<Rgba32>(png);
        var (left, top, right, bottom) = (int.MaxValue, int.MaxValue, 0, 0);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                if (pixel.A < 128 || pixel.R + pixel.G + pixel.B > 384)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        if (right == 0)
        {
            return (0, 0);
        }

        return (right - left, bottom - top);
    }

    static List<PlacedRun> PlacedRuns(ParsedDocument document, string fontDirectory) =>
        Layout(document, fontDirectory)
            .Pages[0]
            .Items
            .OfType<PlacedLine>()
            .SelectMany(_ => _.Runs)
            .ToList();

    static LaidOutDocument Layout(ParsedDocument document, string fontDirectory)
    {
        using var resolver = LayoutFonts.CreateResolver(fontDirectory, null);
        var measurer = new CanonicalParagraphMeasurer(LayoutFonts.ToDelegate(resolver));
        return new Fragmenter(measurer, document.Compatibility)
            .Layout(
                document.Elements,
                document.PageSettings,
                document.Header,
                document.Footer,
                document.FirstPageHeader,
                document.FirstPageFooter,
                document.EvenPageHeader,
                document.EvenPageFooter,
                DocumentNotes.From(document));
    }

    static ParagraphElement Paragraph(string runs) =>
        Document(runs).Elements.OfType<ParagraphElement>().First();

    static ParsedDocument Document(string runs)
    {
        using var stream = new MemoryStream(BuildDocx(runs));
        return new DocumentParser().Parse(stream);
    }

    // A 300 x 160pt page with 20pt margins, holding one paragraph of the given runs.
    static byte[] BuildDocx(string runs) =>
        BuildZip(
            ("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """),
            ("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """),
            ("word/document.xml",
                $"""
                 <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                 <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                   <w:body>
                     <w:p>{runs}</w:p>
                     <w:sectPr>
                       <w:pgSz w:w="6000" w:h="3200"/>
                       <w:pgMar w:top="400" w:right="400" w:bottom="400" w:left="400" w:header="0" w:footer="0" w:gutter="0"/>
                     </w:sectPr>
                   </w:body>
                 </w:document>
                 """));

    static byte[] BuildZip(params (string Name, string Content)[] entries)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                using var writer = new StreamWriter(stream);
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }
}
