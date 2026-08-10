/// <summary>
/// Tests the document-default run size cascade (docDefaults/rPrDefault/w:sz).
///
/// The distinction that matters is between a document that declares no document-wide defaults at
/// all — which inherits normal.dotm's built-in size — and one that declares docDefaults but omits
/// w:sz, which Word reads as the spec's 20 half-points. This mirrors the existing pPrDefault rule,
/// where an omitted paragraph spacing means zero rather than the built-in 8pt.
/// </summary>
public class DocDefaultFontSizeTests
{
    [Test]
    public async Task DocDefaultsWithoutSize_UsesSpecTenPoint()
    {
        // brochures/05 declares <w:docDefaults> (in styles2.xml) but no w:sz anywhere — neither in
        // rPrDefault nor in the Normal style. Word-probe confirmed: injecting an explicit
        // w:sz="20" reproduces Word's render with zero differing text pixels, while w:sz="24"
        // repaginates the document from 4 pages to 5.
        var doc = Parse("brochures", "05");

        var run = Paragraphs(doc)
            .SelectMany(_ => _.Runs)
            .First(_ => _.Text.StartsWith("City Center Catering is a premier"));

        await Assert.That(run.Properties.FontSizePoints).IsEqualTo(10.0);
    }

    [Test]
    public async Task DocDefaultsWithSize_UsesDeclaredSize()
    {
        // agendas-minutes/03 declares w:sz="22" in rPrDefault and its Normal style does not
        // override it, so the declared size is the one that reaches the run.
        var doc = Parse("agendas-minutes", "03");

        var run = Paragraphs(doc)
            .SelectMany(_ => _.Runs)
            .First(_ => _.Text.StartsWith(". Attendees included"));

        await Assert.That(run.Properties.FontSizePoints).IsEqualTo(11.0);
    }

    [Test]
    public async Task StyleSizeOverridesDocDefault()
    {
        // agendas-minutes/07 declares w:sz="22" in rPrDefault but its Normal style declares
        // w:sz="20", which wins. Guards the spec default from being mistaken for a style default.
        var doc = Parse("agendas-minutes", "07");

        var run = Paragraphs(doc)
            .SelectMany(_ => _.Runs)
            .First(_ => _.Text.StartsWith("The minutes were read from the August meeting"));

        await Assert.That(run.Properties.FontSizePoints).IsEqualTo(10.0);
    }

    [Test]
    public async Task NoStylesPart_KeepsBuiltInTwelvePoint()
    {
        // long_paragraph is document.xml only — no styles part, so no docDefaults to read. Word
        // supplies normal.dotm's built-in Normal, whose advances solve to 12pt Aptos.
        var doc = Parse("long_paragraph");

        var run = Paragraphs(doc)
            .SelectMany(_ => _.Runs)
            .First(_ => _.Text.Length > 0);

        await Assert.That(run.Properties.FontSizePoints).IsEqualTo(12.0);
    }

    static ParsedDocument Parse(params string[] scenario)
    {
        var parts = new[] { ProjectFiles.ProjectDirectory, "Inputs", "word" }
            .Concat(scenario)
            .Append("input.docx")
            .ToArray();
        using var stream = File.OpenRead(Path.Combine(parts));
        return new DocumentParser().Parse(stream);
    }

    // Body paragraphs plus those nested in table cells — the fixtures lay their text out both ways.
    static IEnumerable<ParagraphElement> Paragraphs(ParsedDocument doc) =>
        doc.Elements.OfType<ParagraphElement>()
            .Concat(doc.Elements
                .OfType<TableElement>()
                .SelectMany(_ => _.Rows)
                .SelectMany(_ => _.Cells)
                .SelectMany(_ => _.Content)
                .OfType<ParagraphElement>());
}
