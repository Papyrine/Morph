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
            Properties = new()
        };

    public static ParagraphElement Heading(int level, string text) =>
        new()
        {
            Runs = [TextRun(text)],
            Properties = new()
            {
                StyleId = $"Heading{level}"
            }
        };

    public static ParagraphElement Aligned(TextAlignment alignment, string text) =>
        new()
        {
            Runs = [TextRun(text)],
            Properties = new()
            {
                Alignment = alignment
            }
        };

    public static ParagraphElement ListItem(string marker, double indentPoints, string text) =>
        new()
        {
            Runs = [TextRun(text)],
            Properties = new()
            {
                Numbering = new()
                {
                    Text = marker,
                    IndentPoints = indentPoints
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
