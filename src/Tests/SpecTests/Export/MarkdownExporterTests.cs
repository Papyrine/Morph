/// <summary>
/// Low-level tests for <see cref="MarkdownExporter"/>. Output is snapshotted; the syntax for each
/// case was cross-checked against Pandoc's DOCX → Markdown behaviour.
/// </summary>
public class MarkdownExporterTests
{
    [Test]
    public Task Paragraph()
    {
        var export = MarkdownExporter.Export(Doc(Para(TextRun("Hello world"))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task Headings()
    {
        var export = MarkdownExporter.Export(
            Doc(
                Heading(1, "Title"),
                Heading(2, "Section"),
                Heading(3, "Subsection")));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task InlineFormatting()
    {
        var export = MarkdownExporter.Export(
            Doc(
                Para(
                    TextRun("normal "),
                    TextRun("bold", bold: true),
                    TextRun(" "),
                    TextRun("italic", italic: true),
                    TextRun(" "),
                    TextRun("under", underline: true),
                    TextRun(" "),
                    TextRun("struck", strike: true))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task BoldItalicCombined()
    {
        var export = MarkdownExporter.Export(
            Doc(
                Para(TextRun("both", bold: true, italic: true))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task SuperAndSubscript()
    {
        var export = MarkdownExporter.Export(
            Doc(
                Para(
                    TextRun("E = mc"),
                    TextRun("2", vertical: VerticalRunAlignment.Superscript),
                    TextRun(" and H"),
                    TextRun("2", vertical: VerticalRunAlignment.Subscript),
                    TextRun("O"))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task EscapesMarkdownSpecials()
    {
        var export = MarkdownExporter.Export(
            Doc(
                Para(TextRun("a * b _ c [d] `code`"))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task TrailingSpacesStayOutsideEmphasis()
    {
        var export = MarkdownExporter.Export(
            Doc(
                Para(
                    TextRun("lead "),
                    TextRun("bold ", bold: true),
                    TextRun("tail"))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task Hyperlink()
    {
        var export = MarkdownExporter.Export(
            Doc(
                Para(
                    TextRun("see "),
                    TextRun("the docs", url: "https://example.com/docs"),
                    TextRun(" now"))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task UnorderedList()
    {
        var export = MarkdownExporter.Export(
            Doc(
                ListItem("•", 18, "first"),
                ListItem("•", 18, "second")));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task OrderedList()
    {
        var export = MarkdownExporter.Export(
            Doc(
                ListItem("1.", 18, "one"),
                ListItem("2.", 18, "two")));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task NestedList()
    {
        var export = MarkdownExporter.Export(
            Doc(
                ListItem("1.", 18, "outer one"),
                ListItem("•", 54, "inner bullet"),
                ListItem("2.", 18, "outer two")));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task TableWithHeader()
    {
        var export = MarkdownExporter.Export(
            Doc(
                Table(
                    Row(header: true, "Name", "Value"),
                    Row(header: false, "alpha", "1"),
                    Row(header: false, "beta", "2"))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task TableEscapesPipes()
    {
        var export = MarkdownExporter.Export(
            Doc(
                Table(
                    Row(header: true, "a|b", "c"),
                    Row(header: false, "d", "e|f"))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task HorizontalRule()
    {
        var export = MarkdownExporter.Export(
            Doc(
                Para(TextRun("above")),
                new HorizontalRuleElement(),
                Para(TextRun("below"))));
        return Verify(export, extension: "md");
    }
}
