using static ExportTestBuilders;

/// <summary>
/// Low-level tests for <see cref="MarkdownExporter"/>. Output is snapshotted; the syntax for each
/// case was cross-checked against Pandoc's DOCX → Markdown behaviour.
/// </summary>
public class MarkdownExporterTests
{
    static SettingsTask VerifyMarkdown(ParsedDocument document) =>
        Verify(MarkdownExporter.Export(document));

    [Test]
    public Task Paragraph() =>
        VerifyMarkdown(Doc(Para(TextRun("Hello world"))));

    [Test]
    public Task Headings() =>
        VerifyMarkdown(Doc(
            Heading(1, "Title"),
            Heading(2, "Section"),
            Heading(3, "Subsection")));

    [Test]
    public Task InlineFormatting() =>
        VerifyMarkdown(Doc(Para(
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
        VerifyMarkdown(Doc(Para(TextRun("both", bold: true, italic: true))));

    [Test]
    public Task SuperAndSubscript() =>
        VerifyMarkdown(Doc(Para(
            TextRun("E = mc"),
            TextRun("2", vertical: VerticalRunAlignment.Superscript),
            TextRun(" and H"),
            TextRun("2", vertical: VerticalRunAlignment.Subscript),
            TextRun("O"))));

    [Test]
    public Task EscapesMarkdownSpecials() =>
        VerifyMarkdown(Doc(Para(TextRun("a * b _ c [d] `code`"))));

    [Test]
    public Task TrailingSpacesStayOutsideEmphasis() =>
        VerifyMarkdown(Doc(Para(
            TextRun("lead "),
            TextRun("bold ", bold: true),
            TextRun("tail"))));

    [Test]
    public Task Hyperlink() =>
        VerifyMarkdown(Doc(Para(
            TextRun("see "),
            TextRun("the docs", url: "https://example.com/docs"),
            TextRun(" now"))));

    [Test]
    public Task UnorderedList() =>
        VerifyMarkdown(Doc(
            ListItem("•", 18, "first"),
            ListItem("•", 18, "second")));

    [Test]
    public Task OrderedList() =>
        VerifyMarkdown(Doc(
            ListItem("1.", 18, "one"),
            ListItem("2.", 18, "two")));

    [Test]
    public Task NestedList() =>
        VerifyMarkdown(Doc(
            ListItem("1.", 18, "outer one"),
            ListItem("•", 54, "inner bullet"),
            ListItem("2.", 18, "outer two")));

    [Test]
    public Task TableWithHeader() =>
        VerifyMarkdown(Doc(Table(
            Row(header: true, "Name", "Value"),
            Row(header: false, "alpha", "1"),
            Row(header: false, "beta", "2"))));

    [Test]
    public Task TableEscapesPipes() =>
        VerifyMarkdown(Doc(Table(
            Row(header: true, "a|b", "c"),
            Row(header: false, "d", "e|f"))));

    [Test]
    public Task HorizontalRule() =>
        VerifyMarkdown(Doc(
            Para(TextRun("above")),
            new HorizontalRuleElement(),
            Para(TextRun("below"))));
}
