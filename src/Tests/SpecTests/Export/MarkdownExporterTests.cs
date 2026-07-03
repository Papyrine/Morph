/// <summary>
/// Low-level tests for <see cref="MarkdownExporter"/>. Output is snapshotted.
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
    public Task TitleAndSubtitleBecomeHeadings()
    {
        // Word's Title / Subtitle styles have no heading level of their own; they map to # / ##
        // so the document's own title outranks the section headings below it.
        var export = MarkdownExporter.Export(
            Doc(
                Styled("Title", "My Document"),
                Styled("Subtitle", "A subtitle"),
                Heading(1, "First Section")));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task QuoteBecomesBlockQuote()
    {
        // Consecutive Quote-styled paragraphs collapse into one "> " block quote, separated by a
        // bare ">" line; surrounding body paragraphs stay outside it.
        var export = MarkdownExporter.Export(
            Doc(
                Para(TextRun("Intro")),
                Styled("Quote", TextRun("First quoted line.", italic: true)),
                Styled("Quote", TextRun("Second quoted line.", italic: true)),
                Para(TextRun("Outro"))));
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

    [Test]
    public Task HardLineBreak()
    {
        // w:br arrives as '\n' in run text — body paragraphs render it as a backslash hard break;
        // a break at the very end of the paragraph is dropped.
        var export = MarkdownExporter.Export(
            Doc(
                Para(TextRun("First line\nSecond line")),
                Para(TextRun("trailing break dropped\n"))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task HardLineBreakContinuationEscaped()
    {
        // The continuation line after a hard break starts a new source line, so text that would
        // re-parse as a block construct ("- x", "> y") gets its lead character escaped.
        var export = MarkdownExporter.Export(
            Doc(
                Para(TextRun("Shopping:\n- milk\n- eggs"))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task LineBreakInTableCellBecomesBr()
    {
        // A pipe-table row is a single source line, so a w:br inside a cell becomes an inline
        // <br> (matching the HTML exporter) rather than degrading to a space.
        var export = MarkdownExporter.Export(
            Doc(
                Table(
                    Row(header: true, "a", "b"),
                    Row(header: false, "one\ntwo", "c"))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task MultiParagraphTableCellJoinsWithBr()
    {
        // A pipe-table cell cannot hold real block structure; consecutive paragraphs join with
        // <br> so the paragraph boundaries stay visible.
        var export = MarkdownExporter.Export(
            Doc(
                new TableElement
                {
                    Rows =
                    [
                        Row(header: true, "a"),
                        new TableRow
                        {
                            IsHeader = false,
                            Cells =
                            [
                                new TableCell
                                {
                                    Content =
                                    [
                                        Para(TextRun("first")),
                                        Para(TextRun("second"))
                                    ],
                                    Properties = new()
                                }
                            ]
                        }
                    ]
                }));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task BlankTables()
    {
        // A blank table with a visible border is a decorative divider → thematic break; a blank
        // undecorated table is dropped entirely instead of emitting an empty pipe table.
        static TableElement BlankTable(CellBorders? borders) =>
            new()
            {
                Rows =
                [
                    new TableRow
                    {
                        IsHeader = false,
                        Cells =
                        [
                            new TableCell
                            {
                                Content = [],
                                Properties = new() {Borders = borders}
                            }
                        ]
                    }
                ]
            };

        var export = MarkdownExporter.Export(
            Doc(
                Para(TextRun("above")),
                BlankTable(new() {Bottom = BorderEdge.Default}),
                BlankTable(null),
                Para(TextRun("below"))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task LineBreakInHeadingBecomesBr()
    {
        // w:br arrives as '\n'. An ATX heading is a single line, so the break becomes an inline
        // <br> (matching the HTML exporter) rather than a real newline, which would end the heading.
        var export = MarkdownExporter.Export(
            Doc(Heading(1, "SINCERELY,\nSHEETAL PARMAR")));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task HiddenRunsDropped()
    {
        // Hidden (w:vanish) text is skipped everywhere — including inside a hyperlink, where the
        // two visible fragments merge into one link text.
        var hidden = new Run
        {
            Text = "secret",
            Properties = new()
            {
                Hidden = true
            }
        };
        var hiddenLinked = new Run
        {
            Text = "secret",
            HyperlinkUrl = "https://example.com",
            Properties = new()
            {
                Hidden = true
            }
        };
        var export = MarkdownExporter.Export(
            Doc(
                Para(TextRun("visible "), hidden, TextRun("tail")),
                Para(
                    TextRun("go", url: "https://example.com"),
                    hiddenLinked,
                    TextRun(" here", url: "https://example.com"))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task EscapesBlockStarters()
    {
        var export = MarkdownExporter.Export(
            Doc(
                Para(TextRun("# not a heading")),
                Para(TextRun("> not a quote")),
                Para(TextRun("- not a bullet")),
                Para(TextRun("1998. was a year")),
                Para(TextRun("--- not a rule")),
                Para(TextRun("#hashtag stays"))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task EscapesHtmlAndEntities()
    {
        // '<' would inject raw HTML when the Markdown renders; "&copy;" would decode to ©.
        // A bare ampersand stays untouched.
        var export = MarkdownExporter.Export(
            Doc(
                Para(TextRun("use <b>tags</b> at AT&T for &copy; and &#169;"))));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task OrderedListStartNumber()
    {
        // A startOverride list renders markers "10." / "11."; the export keeps the real ordinals.
        var export = MarkdownExporter.Export(
            Doc(
                ListItem("10.", 18, "ten"),
                ListItem("11.", 18, "eleven")));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task NestedListFromLevels()
    {
        // ListParagraph-styled lists have one flat style indent for every level; nesting must
        // follow the w:ilvl levels, not the visual indent.
        var export = MarkdownExporter.Export(
            Doc(
                LevelListItem("•", 0, "level 0"),
                LevelListItem("o", 1, "level 1"),
                LevelListItem("▪", 2, "level 2"),
                LevelListItem("•", 0, "level 0 again")));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task NestedListFromIndentsAtSameLevel()
    {
        // ListBullet / ListBullet2-style documents: every item is level 0 of its own one-level
        // list, so the nesting exists only in the visual indent.
        static ParagraphElement Item(int level, double indent, string text) =>
            new()
            {
                Runs = [TextRun(text)],
                Properties = new()
                {
                    LeftIndentPoints = indent,
                    Numbering = new()
                    {
                        Text = "•",
                        Level = level
                    }
                }
            };

        var export = MarkdownExporter.Export(
            Doc(
                Item(0, 18, "outer"),
                Item(0, 36, "inner"),
                Item(0, 18, "outer again")));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task NestedListFromParagraphIndents()
    {
        // Indents supplied by direct paragraph formatting only (numbering.xml defines none) —
        // nesting must key off the paragraph's resolved LeftIndentPoints.
        var export = MarkdownExporter.Export(
            Doc(
                DirectIndentListItem("•", 36, "level 1"),
                DirectIndentListItem("•", 72, "level 2"),
                DirectIndentListItem("•", 108, "level 3"),
                DirectIndentListItem("•", 36, "level 1 again")));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task NestedTableFlattenedIntoCell()
    {
        var inner = Table(Row(header: false, "inner a", "inner b"));
        var outer = Table(
            new TableRow
            {
                Cells =
                [
                    new()
                    {
                        Content = [Para(TextRun("outer text")), inner]
                    },
                    Cell("plain")
                ]
            });
        var export = MarkdownExporter.Export(Doc(outer));
        return Verify(export, extension: "md");
    }

    [Test]
    public Task UrlParenthesesEncoded()
    {
        var export = MarkdownExporter.Export(
            Doc(
                Para(TextRun("wiki", url: "https://en.wikipedia.org/wiki/Foo_(bar)"))));
        return Verify(export, extension: "md");
    }
}
