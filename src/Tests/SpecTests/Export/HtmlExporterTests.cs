/// <summary>
/// Low-level tests for <see cref="HtmlExporter"/>. Output is snapshotted; the shape of each case
/// (which tags wrap which content) was cross-checked against Pandoc's DOCX → HTML behaviour.
/// </summary>
public class HtmlExporterTests
{
    static SettingsTask VerifyHtml(ParsedDocument document) =>
        Verify(new Target("html", HtmlExporter.Export(document)));

    [Test]
    public Task Paragraph() =>
        VerifyHtml(Doc(Para(TextRun("Hello world"))));

    [Test]
    public Task Headings() =>
        VerifyHtml(Doc(
            Heading(1, "Title"),
            Heading(2, "Section"),
            Heading(3, "Subsection")));

    [Test]
    public Task DuplicateHeadingIds() =>
        VerifyHtml(Doc(
            Heading(1, "Overview"),
            Heading(2, "Overview")));

    [Test]
    public Task InlineFormatting() =>
        VerifyHtml(Doc(Para(
            TextRun("normal "),
            TextRun("bold", bold: true),
            TextRun(" "),
            TextRun("italic", italic: true),
            TextRun(" "),
            TextRun("under", underline: true),
            TextRun(" "),
            TextRun("struck", strike: true))));

    [Test]
    public Task BoldItalicCombined() =>
        VerifyHtml(Doc(Para(TextRun("both", bold: true, italic: true))));

    [Test]
    public Task SuperAndSubscript() =>
        VerifyHtml(Doc(Para(
            TextRun("E = mc"),
            TextRun("2", vertical: VerticalRunAlignment.Superscript),
            TextRun(" and H"),
            TextRun("2", vertical: VerticalRunAlignment.Subscript),
            TextRun("O"))));

    [Test]
    public Task AllCapsAndColor() =>
        VerifyHtml(Doc(Para(
            TextRun("shout", allCaps: true),
            TextRun(" "),
            TextRun("red", color: "FF0000"))));

    [Test]
    public Task Alignment() =>
        VerifyHtml(Doc(
            Aligned(TextAlignment.Center, "centered"),
            Aligned(TextAlignment.Right, "right"),
            Aligned(TextAlignment.Justify, "justified")));

    [Test]
    public Task Hyperlink() =>
        VerifyHtml(Doc(Para(
            TextRun("see "),
            TextRun("the docs", url: "https://example.com/docs"),
            TextRun(" now"))));

    [Test]
    public Task UnorderedList() =>
        VerifyHtml(Doc(
            ListItem("•", 18, "first"),
            ListItem("•", 18, "second")));

    [Test]
    public Task OrderedList() =>
        VerifyHtml(Doc(
            ListItem("1.", 18, "one"),
            ListItem("2.", 18, "two")));

    [Test]
    public Task NestedList() =>
        VerifyHtml(Doc(
            ListItem("1.", 18, "outer one"),
            ListItem("•", 54, "inner bullet"),
            ListItem("2.", 18, "outer two")));

    [Test]
    public Task BlankParagraphsDropped() =>
        VerifyHtml(Doc(
            Para(TextRun("before")),
            Para(),
            Para(TextRun("   ")),
            Para(TextRun("after"))));

    [Test]
    public Task TableWithHeader() =>
        VerifyHtml(Doc(Table(
            Row(header: true, "Name", "Value"),
            Row(header: false, "alpha", "1"),
            Row(header: false, "beta", "2"))));

    [Test]
    public Task TableColumnSpan()
    {
        var table = Table(
            new TableRow {Cells = [Cell("wide", gridSpan: 2)]},
            new TableRow {Cells = [Cell("a"), Cell("b")]});
        return VerifyHtml(Doc(table));
    }

    [Test]
    public Task TableRowSpan()
    {
        var table = Table(
            new TableRow {Cells = [Cell("tall", merge: VerticalMergeType.Restart), Cell("r1")]},
            new TableRow {Cells = [Cell("", merge: VerticalMergeType.Continue), Cell("r2")]});
        return VerifyHtml(Doc(table));
    }

    [Test]
    public Task HorizontalRule() =>
        VerifyHtml(Doc(
            Para(TextRun("above")),
            new HorizontalRuleElement(),
            Para(TextRun("below"))));

    [Test]
    public Task TableCellBorders()
    {
        // A cell with explicit borders renders them (collapsed to the CSS shorthand); a sibling cell
        // with none stays borderless.
        var table = Table(
            new TableRow
            {
                Cells =
                [
                    new() {Content = [Para(TextRun("bordered"))], Properties = new() {Borders = CellBorders.All}},
                    Cell("plain")
                ]
            });
        return VerifyHtml(Doc(table));
    }

    [Test]
    public Task ParagraphBorder() =>
        VerifyHtml(Doc(new ParagraphElement
        {
            Runs = [TextRun("boxed")],
            Properties = new()
            {
                Borders = CellBorders.All,
                BorderTopSpacePoints = 4,
                BorderRightSpacePoints = 6,
                BorderBottomSpacePoints = 4,
                BorderLeftSpacePoints = 6
            }
        }));
}
