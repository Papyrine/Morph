/// <summary>
/// The heights Excel gives a sheet's rows: on its screen, and on the page it prints.
///
/// <para>A row's height is not printed as declared. Excel lays the sheet out on the screen and maps
/// it onto the printer through the ratio of two standard row heights of the workbook's body font
/// (the Normal style's): the one it measures for the screen and the one it measures for the
/// printer, each in that device's whole pixels. A row prints at</para>
/// <code>round(zoom × floor(u × printerStandard / (4 × screenStandard) + 1/8))</code>
/// <para>printer pixels, where <c>u</c> is the row's height in quarter pixels of the screen.
/// Probed 2026-11-20 over COM and Excel's XPS export, gridlines read as vectors:</para>
/// <list type="bullet">
/// <item><b>The quarter pixel.</b> Excel holds a row height to a quarter of a 120-dpi pixel, 0.15pt,
/// rounded to nearest: saved back, 14.3 is 14.25, 13.9 is 13.95, 33.33 is 33.3 and 100 is 100.05.
/// Only that grid makes one ratio fit every row: on the declared points 15.75 needs a ratio of at
/// least 4.6857 and 33.33 one under 4.6805.</item>
/// <item><b>The rule.</b> <c>floor(u × R / 4 + 1/8)</c> with <c>R = printerStandard / screenStandard</c>
/// fits every custom row of 120 workbooks (20 faces, 8 to 28pt, rows declared 7.5 to 100pt). The
/// offset is a sharp 1/8: the feasible interval across them all is [0.1250, 0.1273). A row Excel
/// sizes itself prints by the same rule, from the screen height it sized it to (Arial 10 body, a
/// 20pt cell's 41px row prints at 192). So does a standard row, which comes out at exactly
/// <c>printerStandard</c>.</item>
/// <item><b>The zoom</b> scales the row as printed at 100%, rounded to nearest: 670 of 670 rows of
/// four faces at 50, 63, 75 and 88%, where scaling the unrounded height, or flooring, miss up to
/// half of them.</item>
/// <item><b>The two standard heights</b> follow from no table of the font: the screen one is
/// measured through DirectWrite and the printer one through GDI with extras on some faces. They
/// are Excel's own numbers, asked of it by <c>scripts/excel-row-heights</c> and shipped in
/// <c>ExcelRowHeights.txt</c>; a face the table lacks takes GDI's cell plus 2 screen and 8 printer
/// pixels, which is the printer's rule exactly on most faces and the screen's on the Calibri
/// family from 12 to 31 ppem.</item>
/// </list>
/// <para>The ratio is why the same declared heights printed 6% short under an Arial 10 body
/// (103/600 over 22/120 = 0.936) and 6% long under Verdana 11 (122/600 over 23/120 = 1.061),
/// saw-toothing with the size as the two standard heights step at different sizes. The rule
/// reproduces all 17 of the factors measured on 2026-11-01 from another workbook's package (30pt
/// rows under Arial 8 to 16, Calibri 8 to 14, Constantia, Georgia, Segoe UI, Times New Roman and
/// Verdana), before the mechanism was known: Arial 9's 0.976 is 244 of 250 pixels, Calibri 8's
/// 1.056 is 264. It belongs to a
/// 120-dpi screen and a 600-dpi printer, the machine the references are rendered on: on a 96-dpi
/// screen Arial 10's standard height is 17px and the same rows print 3% short instead.</para>
/// </summary>
sealed class ExcelRowHeights
{
    const int screenDpi = 120;
    const int printerDpi = 600;

    /// <summary>A quarter of a 120-dpi pixel, the grid Excel holds a row height to.</summary>
    const double quarterPoints = 72.0 / screenDpi / 4;

    readonly Func<string, bool, bool, FontMetrics?> resolveFont;
    readonly Dictionary<(string Family, double Size), (int Screen, int Printer)> standards = [];
    readonly int screenStandard;
    readonly int printerStandard;

    public ExcelRowHeights(string family, double sizePoints, Func<string, bool, bool, FontMetrics?> resolveFont)
    {
        this.resolveFont = resolveFont;
        (screenStandard, printerStandard) = StandardOf(family, sizePoints);
    }

    /// <summary>
    /// A row declared <paramref name="points"/> tall: as printed at <paramref name="zoomPercent"/>,
    /// or, drawn rather than printed (null), exactly as declared — the quarter pixel is the 120-dpi
    /// screen's, which only the print path has a reference to match, and a drawn sheet's rows are
    /// the heights its author gave them.
    /// </summary>
    public double Declared(double points, int? zoomPercent)
    {
        if (zoomPercent == null)
        {
            return Math.Max(0, points);
        }

        return Height(Quarters(points), zoomPercent);
    }

    /// <summary>
    /// A row Excel sizes itself: as tall as the standard height of its tallest font, and never
    /// shorter than the body font's.
    /// </summary>
    public double Auto(IEnumerable<(string Family, double SizePoints)> fonts, int? zoomPercent)
    {
        var pixels = screenStandard;
        foreach (var (family, size) in fonts)
        {
            pixels = Math.Max(pixels, StandardOf(family, size).Screen);
        }

        return Height(4L * pixels, zoomPercent);
    }

    /// <summary>A row of the body font's standard height.</summary>
    public double Standard(int? zoomPercent) =>
        Height(4L * screenStandard, zoomPercent);

    static long Quarters(double points)
    {
        if (points <= 0)
        {
            return 0;
        }

        return (long) Math.Round(points / quarterPoints, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The height in points of a row <paramref name="quarters"/> quarter pixels tall on the screen:
    /// as Excel prints it at <paramref name="zoomPercent"/>, or as that screen lays it out when the
    /// sheet is drawn rather than printed (null).
    /// </summary>
    double Height(long quarters, int? zoomPercent)
    {
        if (zoomPercent is not { } zoom)
        {
            return quarters * quarterPoints;
        }

        // floor(u × printer / (4 × screen) + 1/8), in integers
        var full = (2 * quarters * printerStandard + screenStandard) / (8L * screenStandard);
        var zoomed = (full * zoom + 50) / 100;
        return zoomed * 72.0 / printerDpi;
    }

    (int Screen, int Printer) StandardOf(string family, double sizePoints)
    {
        if (standards.TryGetValue((family, sizePoints), out var cached))
        {
            return cached;
        }

        var found = ExcelStandardHeights.Lookup(family, sizePoints) ?? Measured(family, sizePoints);
        standards[(family, sizePoints)] = found;
        return found;
    }

    /// <summary>
    /// For a face the table lacks: GDI's cell (<see cref="FontMetrics.GdiCell"/>) plus 2 pixels on
    /// the screen and 8 on the printer, at the ppem Excel creates the font at on each.
    /// </summary>
    (int Screen, int Printer) Measured(string family, double sizePoints)
    {
        if (resolveFont(family, false, false) is not { UnitsPerEm: > 0 } metrics)
        {
            return ExcelStandardHeights.Fallback(sizePoints);
        }

        var (screen, screenLeading) = metrics.GdiCell(Ppem(sizePoints, screenDpi));
        var (printer, printerLeading) = metrics.GdiCell(Ppem(sizePoints, printerDpi));
        return (screen + screenLeading + 2, printer + printerLeading + 8);
    }

    static int Ppem(double sizePoints, int dpi) =>
        (int) Math.Round(sizePoints * dpi / 72, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Excel's standard row heights for the families the bundled fonts declare, read from
/// <c>ExcelRowHeights.txt</c>: for each family, the 120-dpi screen's and the 600-dpi printer's, in
/// whole pixels, for 6 to 36pt in half points. A size between two is taken at the nearer, and one
/// outside them is the end's scaled by ppem.
/// </summary>
static class ExcelStandardHeights
{
    const double smallest = 6;
    const double largest = 36;
    const int count = 61;

    static readonly Lazy<Dictionary<string, (int[] Screen, int[] Printer)>> table = new(Load);

    public static (int Screen, int Printer)? Lookup(string family, double sizePoints)
    {
        if (!table.Value.TryGetValue(family, out var heights))
        {
            return null;
        }

        return At(heights, sizePoints);
    }

    /// <summary>For a face nothing resolves: Calibri's numbers, Excel's own default body font.</summary>
    public static (int Screen, int Printer) Fallback(double sizePoints) =>
        At(table.Value["Calibri"], sizePoints);

    static (int Screen, int Printer) At((int[] Screen, int[] Printer) heights, double sizePoints)
    {
        var clamped = Math.Clamp(sizePoints, smallest, largest);
        var index = (int) Math.Round((clamped - smallest) * 2, MidpointRounding.AwayFromZero);
        var screen = heights.Screen[index];
        var printer = heights.Printer[index];
        if (clamped == sizePoints)
        {
            return (screen, printer);
        }

        // past the table's ends the heights grow with the em, which is what they do along it
        return (
            (int) Math.Round(screen * Ppem(sizePoints, 120) / (double) Ppem(clamped, 120), MidpointRounding.AwayFromZero),
            (int) Math.Round(printer * Ppem(sizePoints, 600) / (double) Ppem(clamped, 600), MidpointRounding.AwayFromZero));
    }

    static int Ppem(double sizePoints, int dpi) =>
        Math.Max(1, (int) Math.Round(sizePoints * dpi / 72, MidpointRounding.AwayFromZero));

    static Dictionary<string, (int[] Screen, int[] Printer)> Load()
    {
        var assembly = typeof(ExcelStandardHeights).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(_ => _.EndsWith("ExcelRowHeights.txt", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var result = new Dictionary<string, (int[] Screen, int[] Printer)>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 ||
                line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split('\t');
            var screen = fields[1].Split(" ").Select(_ => int.Parse(_, CultureInfo.InvariantCulture)).ToArray();
            var printer = fields[2].Split(" ").Select(_ => int.Parse(_, CultureInfo.InvariantCulture)).ToArray();
            if (screen.Length != count ||
                printer.Length != count)
            {
                throw new InvalidOperationException($"ExcelRowHeights.txt: {fields[0]} has {screen.Length} and {printer.Length} heights, not {count}.");
            }

            result[fields[0]] = (screen, printer);
        }

        return result;
    }
}
