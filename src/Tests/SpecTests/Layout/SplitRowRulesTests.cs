/// <summary>
/// Two rules of a table row that meets the bottom of its page, each Word-probed 2026-10-11 with baselines
/// read off the XPS: what a cell continued overleaf opens with (<c>_probe_cellcont_*</c>), and when a row
/// with a declared height splits at all (<c>_probe_floorsplit_*</c>).
/// </summary>
public class SplitRowRulesTests
{
    static readonly PageSettings page = new() {WidthPoints = 400, HeightPoints = 400, MarginTop = 40, MarginBottom = 40, MarginLeft = 40, MarginRight = 40};

    static ParagraphElement Paragraph(string text, double before = 0, double after = 0) => new()
    {
        Runs = [new() {Text = text, Properties = new() {FontFamily = "Arial", FontSizePoints = 10}}],
        Properties = new() {SpacingBeforePoints = before, SpacingAfterPoints = after, LineSpacingRule = LineSpacingRule.Exactly, LineSpacingPoints = 20}
    };

    static TableElement Table(IReadOnlyList<DocumentElement> content, double? floor = null) => new()
    {
        Rows = [new() {Cells = [new() {Properties = new(), Content = [.. content]}], HeightPoints = floor}],
        Properties = new() {GridColumnWidths = [320]}
    };

    static List<PlacedLine> CellLines(LaidOutPage laidOutPage) =>
        [.. laidOutPage.Items.OfType<PlacedTableRow>().SelectMany(_ => _.Cells).SelectMany(_ => _.Content).OfType<PlacedLine>()];

    static string Text(PlacedLine line) =>
        string.Concat(line.Runs.Select(_ => _.Text));

    // The first line overleaf, measured from where a cell with no spacing at all opens.
    static float OpeningOverleaf(double before, double after)
    {
        var spaced = new Fragmenter(LayoutTestFonts.Measurer).Layout([Table([.. Enumerable.Range(0, 40).Select(_ => Paragraph($"para {_}", before, after))])], page);
        var plain = new Fragmenter(LayoutTestFonts.Measurer).Layout([Table([.. Enumerable.Range(0, 40).Select(_ => Paragraph($"para {_}"))])], page);
        return CellLines(spaced.Pages[1])[0].Y - CellLines(plain.Pages[1])[0].Y;
    }

    // Word, against a cell with no spacing at 89.5pt: 30 before and nothing after opens at 118.9, with 20
    // after at 99.7, with 30 after at 89.5, and 20 before with 30 after at 89.5.
    [Test]
    [Arguments(30, 0, 30)]
    [Arguments(30, 20, 10)]
    [Arguments(30, 30, 0)]
    [Arguments(20, 30, 0)]
    [Arguments(0, 30, 0)]
    public async Task A_paragraph_opening_a_continued_cell_keeps_what_its_before_adds_to_the_after_ahead(double before, double after, double kept) =>
        await Assert.That(OpeningOverleaf(before, after)).IsEqualTo((float) kept).Within(0.01f);

    // _probe_cellcont_b: a 30pt-before paragraph cut by the break carries on at 89.5, with none of it.
    [Test]
    public async Task A_paragraph_resumed_part_way_takes_no_spacing()
    {
        var words = string.Join(" ", Enumerable.Range(0, 700).Select(_ => $"word{_}"));
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout([Table([Paragraph("lead"), Paragraph(words, before: 30)])], page);
        var plain = new Fragmenter(LayoutTestFonts.Measurer).Layout([Table([Paragraph("lead"), Paragraph(words)])], page);

        await Assert.That(laidOut.Pages.Count).IsGreaterThan(1);
        await Assert.That(CellLines(laidOut.Pages[1])[0].Y).IsEqualTo(CellLines(plain.Pages[1])[0].Y).Within(0.01f);
    }

    // Twelve one-line paragraphs (240pt) behind filler that leaves 68pt: three lines and nine with no
    // declared height and under a 40pt one, the whole row overleaf under a 100pt one; with 128pt left the
    // 100pt row splits six and six.
    [Test]
    [Arguments(13, null, 3)]
    [Arguments(13, 40, 3)]
    [Arguments(13, 100, 0)]
    [Arguments(10, 100, 6)]
    public async Task A_row_splits_only_where_its_declared_height_fits_what_is_left(int fillerLines, int? floor, int linesKept)
    {
        // 320pt of content area; filler of exact 20pt lines leaves 320 - 20n above the row.
        var filler = Enumerable.Range(0, fillerLines).Select(DocumentElement (_) => Paragraph($"fill {_}"));
        var row = Table([.. Enumerable.Range(0, 12).Select(_ => Paragraph($"cell {_}"))], floor);

        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout([.. filler, row], page);

        await Assert.That(CellLines(laidOut.Pages[0]).Count).IsEqualTo(linesKept);
        await Assert.That(CellLines(laidOut.Pages[1]).Count).IsEqualTo(12 - linesKept);
        await Assert.That(Text(CellLines(laidOut.Pages[1])[0])).IsEqualTo($"cell {linesKept}");
    }
}
