using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// Pins where Word draws a tab leader's glyphs (<see cref="LeaderTiling"/>), against the numbers read
/// off Word's XPS of <c>_probe_leader</c> on 2026-11-03: every font and size a leader was drawn at,
/// and where its first and last glyphs stood.
/// </summary>
public class LeaderTilingTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static FontMetrics Read(string file) =>
        FontMetricsReader.Read(Path.Combine(fontsDirectory, file))
        ?? throw new InvalidOperationException($"Could not read metrics from {file}");

    // Word's dot pitch, in pixels of its 120-dpi grid, for each size it was probed at: the dot's
    // advance at the em rounded to a whole pixel, itself rounded to a whole pixel. Times New Roman's
    // dot came out pixel for pixel as Calibri's.
    [Test]
    [Arguments("Calibri_400.ttf", new[] {8, 9, 10, 11, 12, 13, 14, 16, 18, 20, 24, 28, 36, 48}, new[] {3, 4, 4, 5, 5, 6, 6, 7, 8, 8, 10, 12, 15, 20})]
    [Arguments("Times_New_Roman_400.ttf", new[] {8, 9, 10, 11, 12, 13, 14, 16, 18, 20, 24, 28, 36, 48}, new[] {3, 4, 4, 5, 5, 6, 6, 7, 8, 8, 10, 12, 15, 20})]
    [Arguments("Arial_400.ttf", new[] {8, 9, 10, 11, 12, 13, 14, 16, 18, 20, 24, 28, 36, 48}, new[] {4, 4, 5, 5, 6, 6, 6, 8, 8, 9, 11, 13, 17, 22})]
    public async Task A_dot_is_drawn_a_whole_pixel_advance_apart(string file, int[] sizes, int[] pixels)
    {
        var metrics = Read(file);
        var drawn = sizes.Select(_ => Math.Round(LeaderTiling.PitchPoints(metrics, '.', _, 0) * 120 / 72, 6)).ToArray();

        await Assert.That(drawn).IsEquivalentTo(pixels.Select(_ => (double) _));
    }

    // The other glyphs, in 24pt Calibri: a hyphen leader 12px apart, a middle dot 10, and an underscore
    // (the glyph Word draws for an underscore leader and a heavy one alike) 20.
    [Test]
    public async Task Every_leader_glyph_takes_its_own_whole_pixel_advance()
    {
        var metrics = Read("Calibri_400.ttf");

        await Assert.That(LeaderTiling.Glyph(TabLeader.Heavy)).IsEqualTo('_');
        await Assert.That(LeaderTiling.PitchPoints(metrics, LeaderTiling.Glyph(TabLeader.Hyphen), 24, 0)).IsEqualTo(7.2);
        await Assert.That(LeaderTiling.PitchPoints(metrics, LeaderTiling.Glyph(TabLeader.MiddleDot), 24, 0)).IsEqualTo(6.0);
        await Assert.That(LeaderTiling.PitchPoints(metrics, LeaderTiling.Glyph(TabLeader.Underscore), 24, 0)).IsEqualTo(12.0);
    }

    // A 24pt Calibri leader's dots are 6pt apart. As the text before its tab grew a letter at a time,
    // its first dot moved from place to place of a 6pt grid of the page — never to the tab's own start
    // — and its last was the last to end before the right-aligned "7" at 510.425pt.
    [Test]
    [Arguments(85.8, 90.0, 70)]
    [Arguments(91.225, 96.0, 69)]
    [Arguments(96.625, 102.0, 68)]
    [Arguments(102.625, 108.0, 67)]
    [Arguments(107.475, 108.0, 67)]
    [Arguments(113.445, 114.0, 66)]
    [Arguments(118.855, 120.0, 65)]
    [Arguments(124.245, 126.0, 64)]
    [Arguments(130.255, 132.0, 63)]
    public async Task A_leader_stands_on_the_pages_grid(double tabStart, double firstDot, int dots)
    {
        var found = LeaderTiling.TryGetRange(tabStart, 510.425 - tabStart, 6, 612, out var startX, out var count);

        await Assert.That(found).IsTrue();
        await Assert.That(startX).IsEqualTo(firstDot);
        await Assert.That(count).IsEqualTo(dots);
    }

    // The same grid wherever the paragraph is: in Word the first dot after "C" stood at 138pt in a
    // table cell starting at 125.15pt (a 75pt margin and a 50.15pt first column), on the page's 6pt
    // grid and not on one counted from the cell. Drawn end to end through the layout and the SVG
    // painter, which writes each dot's x.
    [Test]
    public async Task A_leader_in_a_table_cell_stands_on_the_pages_grid_too()
    {
        using var stream = BuildCellDocument();
        var parsed = DocumentConverter.Parse(stream, null);
        var options = new ImageExportOptions
        {
            Dpi = 96,
            FontDirectory = fontsDirectory
        };
        var laidOut = ImageSharpDocumentConverter.Layout(parsed, options);
        using var context = ImageSharpDocumentConverter.CreateContext(parsed, options);
        var markup = SvgPainter.PaintPage(laidOut.Pages[0], context, "Morph Text").Markup;

        var leader = Regex.Match(markup, "<text x=\"([^\"]*)\"[^>]*>\\.+</text>");
        await Assert.That(leader.Success).IsTrue();
        var xs = leader.Groups[1].Value.Split(' ').Select(_ => double.Parse(_, CultureInfo.InvariantCulture)).ToList();
        await Assert.That(xs[0]).IsEqualTo(138.0);
        await Assert.That(xs.All(_ => Math.Abs(_ - 6 * Math.Round(_ / 6)) < 0.01)).IsTrue();
    }

    // The probe's table: a 75pt margin, a 1003-twip first column with no cell margins, and in the
    // second cell "C", a tab and "7" in 24pt Calibri, the tab running to a right stop with a dot leader.
    static MemoryStream BuildCellDocument()
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            W.Run Run(DocumentFormat.OpenXml.OpenXmlElement content) =>
                new(new W.RunProperties(new W.RunFonts {Ascii = "Calibri", HighAnsi = "Calibri"}, new W.FontSize {Val = "48"}), content);

            var paragraph = new W.Paragraph(
                new W.ParagraphProperties(
                    new W.Tabs(
                        new W.TabStop
                        {
                            Val = W.TabStopValues.Right,
                            Position = 5000,
                            Leader = W.TabStopLeaderCharValues.Dot
                        })),
                Run(new W.Text("C")),
                Run(new W.TabChar()),
                Run(new W.Text("7")));
            var table = new W.Table(
                new W.TableProperties(
                    new W.TableWidth {Width = "7003", Type = W.TableWidthUnitValues.Dxa},
                    new W.TableLayout {Type = W.TableLayoutValues.Fixed},
                    new W.TableCellMarginDefault(
                        new W.TableCellLeftMargin {Width = 0, Type = W.TableWidthValues.Dxa},
                        new W.TableCellRightMargin {Width = 0, Type = W.TableWidthValues.Dxa})),
                new W.TableGrid(new W.GridColumn {Width = "1003"}, new W.GridColumn {Width = "6000"}),
                new W.TableRow(
                    new W.TableCell(new W.TableCellProperties(new W.TableCellWidth {Width = "1003", Type = W.TableWidthUnitValues.Dxa}), new W.Paragraph()),
                    new W.TableCell(new W.TableCellProperties(new W.TableCellWidth {Width = "6000", Type = W.TableWidthUnitValues.Dxa}), paragraph)));
            var section = new W.SectionProperties(
                new W.PageSize {Width = 12240, Height = 15840},
                new W.PageMargin {Top = 720, Right = 720, Bottom = 720, Left = 1500, Header = 0, Footer = 0, Gutter = 0});

            document.AddMainDocumentPart().Document = [with(new W.Body(table, new W.Paragraph(), section))];
        }

        stream.Position = 0;
        return stream;
    }
}
