using DocumentFormat.OpenXml.Packaging;

/// <summary>
/// How <c>w:kern</c> comes down the style ladder. Word-probed 2026-10-02 (<c>_probe_kerncascade</c>):
/// the three packages below are the probe's own, 52 cases across compatibility modes 12 and 15, every
/// run the same <c>ToToToToToToToToToTo</c> read off Word's XPS against directly formatted references
/// (a kerned T advances 19px at 28pt Calibri, an unkerned one 23). Kerning cascades like any other run
/// property — document default, table style, paragraph style with its basedOn chain, character style,
/// the run's own <c>w:rPr</c> — and a package with no docDefaults kerns on every rung.
///
/// <para>The parser used to take the threshold from docDefaults or the run's own <c>w:rPr</c> and
/// from nowhere else, and a run with no <c>w:rPr</c> in a paragraph whose style is defined got none at
/// all: most body text measured unkerned, and broke a word before Word's wherever the kern was what
/// let the word fit.</para>
/// </summary>
public class KerningCascadeTests
{
    const string wNs = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";
    const string text = "ToToToToToToToToToTo";
    const string face = """<w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/>""";
    const string size28 = """<w:sz w:val="56"/>""";

    static string Style(string id, string kind = "paragraph", string? basedOn = null, string rPr = "", bool isDefault = false)
    {
        var defaultAttribute = isDefault ? " w:default=\"1\"" : "";
        var based = basedOn == null ? "" : $"""<w:basedOn w:val="{basedOn}"/>""";
        var runProperties = rPr == "" ? "" : $"<w:rPr>{rPr}</w:rPr>";
        return $"""<w:style w:type="{kind}"{defaultAttribute} w:styleId="{id}"><w:name w:val="{id}"/>{based}{runProperties}</w:style>""";
    }

    static string Paragraph(string? style = null, string rPr = "")
    {
        var paragraphStyle = style == null ? "" : $"""<w:pPr><w:pStyle w:val="{style}"/></w:pPr>""";
        var runProperties = rPr == "" ? "" : $"<w:rPr>{rPr}</w:rPr>";
        return $"<w:p>{paragraphStyle}<w:r>{runProperties}<w:t>{text}</w:t></w:r></w:p>";
    }

    static string Table(string tableStyle, string? style = null) =>
        $"""<w:tbl><w:tblPr><w:tblStyle w:val="{tableStyle}"/></w:tblPr><w:tblGrid><w:gridCol w:w="9000"/></w:tblGrid><w:tr><w:tc>{Paragraph(style)}</w:tc></w:tr></w:tbl>""";

    // docDefaults WITH w:kern 2: kerning is on unless a rung turns it off.
    static readonly string kernedByDefault =
        $"""<w:docDefaults><w:rPrDefault><w:rPr>{face}<w:kern w:val="2"/>{size28}</w:rPr></w:rPrDefault></w:docDefaults>""" +
        Style("Normal", isDefault: true) +
        Style("PlainStyle", basedOn: "Normal") +
        Style("KernOff", rPr: """<w:kern w:val="0"/>""") +
        Style("ChildOfOff", basedOn: "KernOff") +
        Style("KernFrom40", rPr: """<w:kern w:val="80"/>""") +
        Style("Size24From40", basedOn: "KernFrom40", rPr: """<w:sz w:val="48"/>""") +
        Style("Size48From40", basedOn: "KernFrom40", rPr: """<w:sz w:val="96"/>""") +
        Style("KernOnStyle", rPr: """<w:kern w:val="2"/>""") +
        Style("CharOff", kind: "character", rPr: """<w:kern w:val="0"/>""") +
        Style("CharOn", kind: "character", rPr: """<w:kern w:val="2"/>""") +
        Style("TblOff", kind: "table", rPr: """<w:kern w:val="0"/>""");

    // docDefaults WITHOUT w:kern: kerning is off unless a rung turns it on.
    static readonly string unkernedByDefault =
        $"""<w:docDefaults><w:rPrDefault><w:rPr>{face}{size28}</w:rPr></w:rPrDefault></w:docDefaults>""" +
        Style("Normal", isDefault: true, rPr: """<w:kern w:val="2"/>""") +
        Style("NoBase") +
        Style("ChildOfNormal", basedOn: "Normal") +
        Style("KernOnStyle", rPr: """<w:kern w:val="2"/>""") +
        Style("ChildOfOn", basedOn: "KernOnStyle") +
        Style("KernOff", basedOn: "Normal", rPr: """<w:kern w:val="0"/>""") +
        Style("CharOn", kind: "character", rPr: """<w:kern w:val="2"/>""") +
        Style("TblOn", kind: "table", rPr: """<w:kern w:val="2"/>""");

    // NO docDefaults at all: Word's built-in default, which kerns.
    static readonly string noDocDefaults =
        Style("PlainOnNormal", basedOn: "Normal", rPr: face + size28) +
        Style("NoBase", rPr: face + size28) +
        Style("KernOff", basedOn: "Normal", rPr: face + size28 + """<w:kern w:val="0"/>""");

    static RunProperties Parse(string styles, string body)
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new($"<w:document {wNs}><w:body>{body}</w:body></w:document>");
            main.AddNewPart<StyleDefinitionsPart>().Styles = new($"<w:styles {wNs}>{styles}</w:styles>");
        }

        stream.Position = 0;
        var parsed = new DocumentParser().Parse(stream);
        var paragraph = parsed.Elements[0] switch
        {
            TableElement table => table.Rows[0].Cells[0].Content.OfType<ParagraphElement>().Single(),
            var element => (ParagraphElement) element
        };
        return paragraph.Runs.Single(_ => _.Text == text).Properties;
    }

    // Whether the measurer kerns the run, asked of the measurer itself: its width is the kerned width of
    // the text at the run's size or the unkerned one, and for ten "To" the two are 9% apart.
    static async Task AssertKerned(RunProperties properties, bool kerned)
    {
        var metrics = LayoutTestFonts.Resolve(properties.FontFamily, properties.Bold, properties.Italic)!;
        var expected = CanonicalTextMeasurer.MeasureWidthPoints(metrics, text, properties.FontSizePoints, kerning: kerned);
        var other = CanonicalTextMeasurer.MeasureWidthPoints(metrics, text, properties.FontSizePoints, kerning: !kerned);

        await Assert.That(Math.Abs(expected - other)).IsGreaterThan(10);
        await Assert.That((double) LayoutTestFonts.Measurer.MeasureRunWidth(text, properties)).IsEqualTo(expected).Within(0.01);
    }

    public static IEnumerable<(string Body, bool Kerned)> KernedByDefaultCases() =>
    [
        // A1: no style and no w:rPr, so the document default.
        (Paragraph(), true),
        // A2: a defined style that says nothing about kerning.
        (Paragraph("PlainStyle"), true),
        // A3: the style switches it off.
        (Paragraph("KernOff"), false),
        // A4: a style based on one that switches it off.
        (Paragraph("ChildOfOff"), false),
        // A5 and A6: a 40pt threshold, under 24pt text and under 48pt text.
        (Paragraph("Size24From40"), false),
        (Paragraph("Size48From40"), true),
        // A7 and A8: a character style outranks the paragraph style, either way.
        (Paragraph("PlainStyle", """<w:rStyle w:val="CharOff"/>"""), false),
        (Paragraph("KernOff", """<w:rStyle w:val="CharOn"/>"""), true),
        // A9: the run's own w:kern outranks its paragraph style.
        (Paragraph("KernOff", """<w:kern w:val="2"/>"""), true),
        // A10: a table style outranks the document default.
        (Table("TblOff", "PlainStyle"), false),
        // A11: a paragraph style that declares kerning outranks the table style.
        (Table("TblOff", "KernOnStyle"), true),
        // A12: no paragraph style, so Normal, which says nothing and leaves the table style standing.
        (Table("TblOff"), false)
    ];

    [Test]
    [MethodDataSource(nameof(KernedByDefaultCases))]
    public async Task Kerning_in_docDefaults_holds_until_a_rung_switches_it_off(string body, bool kerned) =>
        await AssertKerned(Parse(kernedByDefault, body), kerned);

    public static IEnumerable<(string Body, bool Kerned)> UnkernedByDefaultCases() =>
    [
        // B1: no style named, so Normal, which declares kerning.
        (Paragraph(), true),
        // B2: a style with no basedOn stands on the document default alone.
        (Paragraph("NoBase"), false),
        // B3: a style based on Normal.
        (Paragraph("ChildOfNormal"), true),
        // B4 and B5: a style that declares kerning, and one based on it.
        (Paragraph("KernOnStyle"), true),
        (Paragraph("ChildOfOn"), true),
        // B6: a character style switches it on over an unkerned paragraph style.
        (Paragraph("NoBase", """<w:rStyle w:val="CharOn"/>"""), true),
        // B7: a style based on Normal that switches it back off.
        (Paragraph("KernOff"), false),
        // B8: a table style switches it on under a paragraph style that says nothing.
        (Table("TblOn", "NoBase"), true),
        // B9: a paragraph style declaring w:kern 0 outranks the table style.
        (Table("TblOn", "KernOff"), false),
        // B10: the table style and Normal both declare it.
        (Table("TblOn"), true)
    ];

    [Test]
    [MethodDataSource(nameof(UnkernedByDefaultCases))]
    public async Task Without_kerning_in_docDefaults_a_rung_has_to_switch_it_on(string body, bool kerned) =>
        await AssertKerned(Parse(unkernedByDefault, body), kerned);

    public static IEnumerable<(string Body, bool Kerned)> NoDocDefaultsCases() =>
    [
        // C1: no style; the run declares only its face and size.
        (Paragraph(rPr: face + size28), true),
        // C2: a style based on a Normal the package does not define, and a run with no w:rPr. This is
        // complex_spacing's CustomStyle1, which measured unkerned.
        (Paragraph("PlainOnNormal"), true),
        // C3: a style with no basedOn.
        (Paragraph("NoBase"), true),
        // C4: a style that switches kerning off.
        (Paragraph("KernOff"), false)
    ];

    [Test]
    [MethodDataSource(nameof(NoDocDefaultsCases))]
    public async Task A_package_with_no_docDefaults_kerns_on_every_rung(string body, bool kerned) =>
        await AssertKerned(Parse(noDocDefaults, body), kerned);

    // The threshold is the resolved rung's own number, not just on or off: business-plans/08 keeps its
    // w:kern on the Normal style, résumés like resumes/01 raise Normal's to 12pt over a 1pt default.
    [Test]
    public async Task The_resolved_threshold_is_the_nearest_rung_s()
    {
        await Assert.That(Parse(kernedByDefault, Paragraph("PlainStyle")).KerningMinFontSizePoints).IsEqualTo(1);
        await Assert.That(Parse(kernedByDefault, Paragraph("Size24From40")).KerningMinFontSizePoints).IsEqualTo(40);
        await Assert.That(Parse(kernedByDefault, Paragraph("ChildOfOff")).KerningMinFontSizePoints).IsEqualTo(0);
        await Assert.That(Parse(unkernedByDefault, Paragraph()).KerningMinFontSizePoints).IsEqualTo(1);
        await Assert.That(Parse(unkernedByDefault, Paragraph("NoBase")).KerningMinFontSizePoints).IsEqualTo(0);
    }
}
