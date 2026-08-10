/// <summary>
/// A table style's own <c>w:pPr</c> applies to every paragraph inside tables that reference it.
/// ECMA-376 resolves a paragraph in a table as
/// <c>docDefaults → table style w:pPr → paragraph style chain → direct w:pPr</c>, so the table
/// style overrides the document defaults but yields to anything the paragraph's style declares.
///
/// Verified against Word on resumes/07, whose tables use TableGrid declaring
/// <c>w:after="0" w:line="240"</c>: sweeping that document's docDefault w:after through 0/160/600
/// leaves its cell rows at 28/28/27px while non-table gaps move sharply, and the same sweep on
/// table_default_style (which has no table-style w:pPr) moves its row pitch 68/100/193.
/// </summary>
public class TableStyleParagraphSpacingTests
{
    [Test]
    public async Task TableStyleSpacing_OverridesDocumentDefaults()
    {
        // resumes/07 declares docDefaults <w:spacing w:after="160" w:line="278"/> but its tables
        // use TableGrid, whose w:pPr is <w:spacing w:after="0" w:line="240" w:lineRule="auto"/>.
        // The cell paragraph carries no pStyle, so nothing outranks the table style.
        var doc = Parse("resumes", "07");

        var cellParagraph = CellParagraphs(doc)
            .First(_ => _.Runs.Any(r => r.Text.StartsWith("Company")));

        await Assert.That(cellParagraph.Properties.SpacingAfterPoints).IsEqualTo(0);
        await Assert.That(cellParagraph.Properties.LineSpacingMultiplier).IsEqualTo(1.0);
    }

    [Test]
    public async Task TableStyleSpacing_DoesNotLeakIntoBodyFlow()
    {
        // Paragraphs OUTSIDE any table must not pick up the table style. Asserted relative to a
        // cell paragraph in the same document rather than against a fixed constant, so the test
        // keeps testing containment even if the surrounding defaults are retuned.
        var doc = Parse("resumes", "07");

        var cell = CellParagraphs(doc)
            .First(_ => _.Runs.Any(r => r.Text.StartsWith("Company")));
        var body = doc.Elements
            .OfType<ParagraphElement>()
            .First(_ => _.Runs.Any(r => r.Text.StartsWith("Full Name")));

        await Assert.That(cell.Properties.SpacingAfterPoints).IsEqualTo(0);
        await Assert.That(body.Properties.SpacingAfterPoints).IsNotEqualTo(0);
        await Assert.That(body.Properties.LineSpacingMultiplier)
            .IsNotEqualTo(cell.Properties.LineSpacingMultiplier);
    }

    [Test]
    public async Task DefaultParagraphStyleDeclaration_OutranksTableStyle()
    {
        // A paragraph with no explicit w:pStyle still uses the document's default paragraph
        // style, and that style's own declarations outrank the table style. business-plans/02's
        // Normal declares w:line="336" (1.4) and its tables use TableGrid declaring w:line="240";
        // Word keeps 1.4 inside those tables. Reading only an explicit pStyle here made the
        // table style win and set the cell text single-spaced, costing 11px on every affected gap.
        var doc = Parse("business-plans", "02");

        var cellParagraph = CellParagraphs(doc)
            .First(_ => _.Runs.Any(r => r.Text.StartsWith("Offerings include")));

        await Assert.That(cellParagraph.Properties.LineSpacingMultiplier).IsEqualTo(1.4);
    }

    [Test]
    public async Task NoTableStylePpr_LeavesCellSpacingAlone()
    {
        // table_default_style's table style declares no w:pPr, so its cell paragraphs keep the
        // built-in 8pt after that Word charges into the row height.
        var doc = Parse("table_default_style");

        var cellParagraph = CellParagraphs(doc).First(_ => _.Runs.Count > 0);

        await Assert.That(cellParagraph.Properties.SpacingAfterPoints).IsEqualTo(8);
    }

    static ParsedDocument Parse(params string[] scenario)
    {
        var parts = new[] {ProjectFiles.ProjectDirectory, "Inputs", "word"}
            .Concat(scenario)
            .Append("input.docx")
            .ToArray();
        using var stream = File.OpenRead(Path.Combine(parts));
        return new DocumentParser().Parse(stream);
    }

    static IEnumerable<ParagraphElement> CellParagraphs(ParsedDocument doc) =>
        doc.Elements
            .OfType<TableElement>()
            .SelectMany(_ => _.Rows)
            .SelectMany(_ => _.Cells)
            .SelectMany(_ => _.Content)
            .OfType<ParagraphElement>();
}
