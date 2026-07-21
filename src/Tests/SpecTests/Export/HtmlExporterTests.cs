/// <summary>
/// Low-level tests for <see cref="HtmlExporter"/>. Output is snapshotted.
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

    /// <summary>
    /// Word's BUILT-IN Heading 4/6 are italic, but the exported stylesheet must not assert that —
    /// the document's own style decides, and of the 12 corpus scenarios using Heading 4 not one
    /// declares italic. A heading whose style really IS italic reaches the exporter as italic runs,
    /// which still emit &lt;em&gt;, so nothing is lost by dropping the CSS assumption.
    /// </summary>
    [Test]
    public Task Heading4NotForcedItalic() =>
        VerifyHtml(Doc(
            Styled("Heading4", TextRun("Upright heading", bold: true)),
            Styled("Heading4", TextRun("Italic heading", bold: true, italic: true))));

    [Test]
    public Task DuplicateHeadingIds() =>
        VerifyHtml(Doc(
            Heading(1, "Overview"),
            Heading(2, "Overview")));

    [Test]
    public Task HeadingBoldSuppressed() =>
        // A heading is bold by default, so a bold run drops its <strong> (inheriting the h1
        // weight); an explicitly non-bold run in the same heading still gets font-weight: normal.
        VerifyHtml(Doc(Styled("Heading1",
            TextRun("Bold part ", bold: true),
            TextRun("normal part"))));

    [Test]
    public Task TitleAndSubtitleBecomeHeadings() =>
        VerifyHtml(Doc(
            Styled("Title", "My Document"),
            Styled("Subtitle", "A subtitle"),
            Heading(1, "First Section")));

    [Test]
    public Task QuoteBecomesBlockQuote() =>
        VerifyHtml(Doc(
            Para(TextRun("Intro")),
            Styled("Quote", TextRun("First quoted line.", italic: true)),
            Styled("Quote", TextRun("Second quoted line.", italic: true)),
            Para(TextRun("Outro"))));

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
            new TableRow
            {
                Cells = [Cell("wide", gridSpan: 2)]
            },
            new TableRow
            {
                Cells = [Cell("a"), Cell("b")]
            });
        return VerifyHtml(Doc(table));
    }

    [Test]
    public Task TableRowSpan()
    {
        var table = Table(
            new TableRow
            {
                Cells = [Cell("tall", merge: VerticalMergeType.Restart), Cell("r1")]
            },
            new TableRow
            {
                Cells = [Cell("", merge: VerticalMergeType.Continue), Cell("r2")]
            });
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
                    new()
                    {
                        Content = [Para(TextRun("bordered"))],
                        Properties = new()
                        {
                            Borders = CellBorders.All
                        }
                    },
                    Cell("plain")
                ]
            });
        return VerifyHtml(Doc(table));
    }

    [Test]
    public Task ParagraphSpacingAndIndent() =>
        // Spacing-after 18 deviates from the 8pt default (emitted); a hanging indent becomes an
        // enlarged left margin plus a negative text-indent; 1.5 line spacing → unitless line-height.
        VerifyHtml(Doc(new ParagraphElement
        {
            Runs = [TextRun("indented")],
            Properties = new()
            {
                SpacingBeforePoints = 12,
                SpacingAfterPoints = 18,
                LeftIndentPoints = 36,
                HangingIndentPoints = 18,
                LineSpacingRule = LineSpacingRule.Auto,
                LineSpacingMultiplier = 1.5
            }
        }));

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
        return VerifyHtml(Doc(
            Para(TextRun("visible "), hidden, TextRun("tail")),
            Para(
                TextRun("go", url: "https://example.com"),
                hiddenLinked,
                TextRun(" here", url: "https://example.com"))));
    }

    [Test]
    public Task OrderedListStartNumber() =>
        // A startOverride list renders markers "10." / "11." — the export keeps the real start.
        VerifyHtml(Doc(
            ListItem("10.", 18, "ten"),
            ListItem("11.", 18, "eleven")));

    [Test]
    public Task NestedListFromLevels() =>
        // ListParagraph-styled lists have one flat style indent for every level; nesting must
        // follow the w:ilvl levels, not the visual indent.
        VerifyHtml(Doc(
            LevelListItem("•", 0, "level 0"),
            LevelListItem("o", 1, "level 1"),
            LevelListItem("▪", 2, "level 2"),
            LevelListItem("•", 0, "level 0 again")));

    [Test]
    public Task NestedListFromParagraphIndents() =>
        // Indents supplied by direct paragraph formatting only (numbering.xml defines none) —
        // nesting must key off the paragraph's resolved LeftIndentPoints.
        VerifyHtml(Doc(
            DirectIndentListItem("•", 36, "level 1"),
            DirectIndentListItem("•", 72, "level 2"),
            DirectIndentListItem("•", 108, "level 3"),
            DirectIndentListItem("•", 36, "level 1 again")));

    [Test]
    public Task ListInTableCell()
    {
        // Numbered cell paragraphs become a real <ul>/<ol> inside the <td>, exactly as at body
        // level — a bulleted agenda cell keeps its bullets instead of flattening to <br />-joined
        // lines.
        var cell = new TableCell
        {
            Content =
            [
                Para(TextRun("intro")),
                ListItem("•", 18, "first"),
                ListItem("•", 18, "second")
            ]
        };
        return VerifyHtml(
            Doc(
                Table(
                    new TableRow
                    {
                        Cells = [cell]
                    })));
    }

    [Test]
    public Task TableRowHeight()
    {
        // An explicit w:trHeight becomes a minimum row height; a row without one stays
        // content-sized.
        var table = Table(
            new TableRow
            {
                HeightPoints = 30,
                Cells = [Cell("tall")]
            },
            Row(header: false, "auto"));
        return VerifyHtml(Doc(table));
    }

    [Test]
    public Task HeadingRunFontSizeLifting() =>
        // A heading whose runs agree on one size carries it on the <hN> itself — otherwise the
        // stylesheet heading size sets the line-box strut and a small-text heading (a 10pt table
        // strip label) renders far taller than Word. Mixed sizes stay per-span under the
        // stylesheet default.
        VerifyHtml(Doc(
            Styled("Heading1", TextRun("small strip label", fontSize: 10)),
            Styled("Heading2", TextRun("mixed ", fontSize: 10), TextRun("sizes", fontSize: 14))));

    [Test]
    public Task LetterSpacing() =>
        // Expanded tracking (w:spacing on the run) becomes letter-spacing; the zero-spacing run
        // stays clean.
        VerifyHtml(Doc(Para(
            TextRun("tracked", characterSpacing: 1),
            TextRun(" normal"))));

    [Test]
    public Task PageGeometry() =>
        // The page margins become the body padding and the content width its max-width, so text
        // wraps at Word's measure.
        VerifyHtml(new()
        {
            PageSettings = new()
            {
                WidthPoints = 612,
                HeightPoints = 792,
                MarginTop = 28.8,
                MarginRight = 36,
                MarginBottom = 72,
                MarginLeft = 36
            },
            Elements = [Para(TextRun("wide measure"))]
        });

    [Test]
    public Task NestedTableInCell()
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
        return VerifyHtml(Doc(outer));
    }

    [Test]
    public Task FootnotesAndEndnotes() =>
        // Footnote and endnote citations become <sup> links, numbered together in reference order,
        // plus a trailing <section class="footnotes"> of definitions with back-links.
        VerifyHtml(new()
        {
            PageSettings = new(),
            Elements =
            [
                Para(TextRun("See note "), FootnoteRef("1"), TextRun(" and "), EndnoteRef("1"), TextRun(".")),
                Para(TextRun("Reuse "), FootnoteRef("1"), TextRun(" here."))
            ],
            Footnotes =
            [
                new()
                {
                    Id = "1",
                    Text = "The footnote body."
                }
            ],
            Endnotes =
            [
                new()
                {
                    Id = "1",
                    Text = "The endnote body."
                }
            ]
        });

    [Test]
    public Task ImageAltText() =>
        // wp:docPr/@descr becomes the <img alt>; HTML-encoded, no escaping of [] needed.
        VerifyHtml(
            Doc(
                new ImageElement
                {
                    ImageData = [1, 2, 3],
                    ContentType = "image/png",
                    WidthPoints = 24,
                    HeightPoints = 12,
                    Description = "A logo [PNG]"
                }));

    [Test]
    public Task ImageInTableCell()
    {
        var cell = new TableCell
        {
            Content =
            [
                Para(TextRun("logo:")),
                new ImageElement
                {
                    ImageData = [1, 2, 3],
                    ContentType = "image/png",
                    WidthPoints = 24,
                    HeightPoints = 12
                }
            ]
        };
        return VerifyHtml(
            Doc(
                Table(
                    new TableRow
                    {
                        Cells = [cell]
                    })));
    }

    [Test]
    public Task ParagraphBorder() =>
        // Symmetric border spaces collapse to the two-value padding shorthand; fully asymmetric
        // ones stay four-part.
        VerifyHtml(
            Doc(
                new ParagraphElement
                {
                    Runs = [TextRun("boxed")],
                    Properties = new()
                    {
                        SpacingAfterPoints = 8,
                        Borders = CellBorders.All,
                        BorderTopSpacePoints = 4,
                        BorderRightSpacePoints = 6,
                        BorderBottomSpacePoints = 4,
                        BorderLeftSpacePoints = 6
                    }
                },
                new ParagraphElement
                {
                    Runs = [TextRun("asymmetric")],
                    Properties = new()
                    {
                        SpacingAfterPoints = 8,
                        Borders = CellBorders.All,
                        BorderTopSpacePoints = 2,
                        BorderRightSpacePoints = 4,
                        BorderBottomSpacePoints = 6,
                        BorderLeftSpacePoints = 8
                    }
                }));

    [Test]
    public Task FloatingShapeGradientFill() =>
        // A gradient-filled floating shape emits a <defs><linearGradient> whose axis is derived from
        // the direction (90° → horizontal: x1=0 → x2=1), then fills the geometry via
        // url(#shape-grad-N). Guards the gradient SVG emission, which nothing under Inputs exercises.
        VerifyHtml(Doc(new FloatingShapeElement
        {
            WidthPoints = 120,
            HeightPoints = 60,
            BehindText = true,
            Gradient = new()
            {
                StartColorHex = "FF0000",
                EndColorHex = "0000FF",
                DirectionDegrees = 90
            }
        }));
}
