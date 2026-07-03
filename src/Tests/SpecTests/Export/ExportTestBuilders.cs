/// <summary>
/// Small builders for assembling <see cref="ParsedDocument"/> trees in the HTML/Markdown exporter
/// tests without the ceremony of full object initializers.
/// </summary>
static class ExportTestBuilders
{
    public static ParsedDocument Doc(params DocumentElement[] elements) =>
        new()
        {
            PageSettings = new(),
            Elements = elements
        };

    public static ParagraphElement Para(params Run[] runs) =>
        new()
        {
            Runs = runs,
            // Word's Normal style resolves to 8pt spacing-after; the parser surfaces that effective
            // value, so fixtures use it too — otherwise the exporter would treat the record's 0
            // default as a deviation and emit a spurious margin-bottom:0pt.
            Properties = new() {SpacingAfterPoints = 8}
        };

    public static ParagraphElement Heading(int level, string text) =>
        new()
        {
            // Word's Heading styles resolve to bold runs; the parser surfaces that as Bold=true, so
            // the fixtures model it too (HTML emits <strong>, Markdown suppresses the redundant **).
            Runs = [TextRun(text, bold: true)],
            Properties = new()
            {
                StyleId = $"Heading{level}"
            }
        };

    // A paragraph carrying a named Word style (e.g. "Title", "Subtitle", "Quote"), the way the
    // parser surfaces w:pStyle. Runs are passed explicitly so a test can set italic etc.
    public static ParagraphElement Styled(string styleId, params Run[] runs) =>
        new()
        {
            Runs = runs,
            Properties = new()
            {
                StyleId = styleId,
                SpacingAfterPoints = 8
            }
        };

    public static ParagraphElement Styled(string styleId, string text) => Styled(styleId, TextRun(text));

    public static ParagraphElement Aligned(TextAlignment alignment, string text) =>
        new()
        {
            Runs = [TextRun(text)],
            Properties = new()
            {
                Alignment = alignment,
                SpacingAfterPoints = 8
            }
        };

    public static ParagraphElement ListItem(string marker, double indentPoints, string text) =>
        new()
        {
            Runs = [TextRun(text)],
            Properties = new()
            {
                // The parser resolves the numbering-level indent into the paragraph's
                // LeftIndentPoints (direct w:ind > style > numbering level); list nesting keys
                // off that resolved value, so the fixture mirrors it.
                LeftIndentPoints = indentPoints,
                Numbering = new()
                {
                    Text = marker,
                    IndentPoints = indentPoints
                }
            }
        };

    /// <summary>
    /// A list item whose indent comes only from direct paragraph formatting (w:ind on the
    /// paragraph), the shape the parser produces when numbering.xml defines no per-level indents
    /// and the paragraphs carry no w:ilvl-derived level (synthetic documents).
    /// <see cref="NumberingInfo.IndentPoints"/> stays 0.
    /// </summary>
    public static ParagraphElement DirectIndentListItem(string marker, double leftIndentPoints, string text) =>
        new()
        {
            Runs = [TextRun(text)],
            Properties = new()
            {
                LeftIndentPoints = leftIndentPoints,
                Numbering = new()
                {
                    Text = marker
                }
            }
        };

    /// <summary>
    /// A list item carrying a real multilevel-list level (w:ilvl) but a flat visual indent — the
    /// shape Word's ListParagraph style produces (one style indent for every level).
    /// </summary>
    public static ParagraphElement LevelListItem(string marker, int level, string text) =>
        new()
        {
            Runs = [TextRun(text)],
            Properties = new()
            {
                LeftIndentPoints = 36,
                Numbering = new()
                {
                    Text = marker,
                    Level = level
                }
            }
        };

    public static Run TextRun(
        string text,
        bool bold = false,
        bool italic = false,
        bool underline = false,
        bool strike = false,
        bool allCaps = false,
        VerticalRunAlignment vertical = VerticalRunAlignment.Baseline,
        string? color = null,
        string? url = null) =>
        new()
        {
            Text = text,
            HyperlinkUrl = url,
            Properties = new()
            {
                Bold = bold,
                Italic = italic,
                Underline = underline,
                Strikethrough = strike,
                AllCaps = allCaps,
                VerticalAlignment = vertical,
                ColorHex = color
            }
        };

    /// <summary>An empty marker run citing a footnote, the shape the parser emits for
    /// <c>w:footnoteReference</c>.</summary>
    public static Run FootnoteRef(string id) => new() {Text = "", FootnoteReferenceId = id};

    /// <summary>An empty marker run citing an endnote (<c>w:endnoteReference</c>).</summary>
    public static Run EndnoteRef(string id) => new() {Text = "", EndnoteReferenceId = id};

    public static TableElement Table(params TableRow[] rows) =>
        new()
        {
            Rows = rows
        };

    public static TableRow Row(bool header, params string[] cells) =>
        new()
        {
            IsHeader = header,
            Cells = cells.Select(_ => Cell(_)).ToArray()
        };

    public static TableCell Cell(string text, int gridSpan = 1, VerticalMergeType merge = VerticalMergeType.None) =>
        new()
        {
            Content = [Para(TextRun(text))],
            Properties = new()
            {
                GridSpan = gridSpan,
                VerticalMerge = merge
            }
        };
}
