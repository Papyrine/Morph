using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// Parses an XLSX package into the shared <see cref="ParsedDocument"/> model.
///
/// A sheet becomes a <see cref="TableElement"/> in the ordinary flow, which is what makes the rest
/// come free: the fragmenter already breaks a long table across pages and already repeats header
/// rows, and Excel's print titles are exactly that. Sheets are separated by section breaks so each
/// can carry its own paper size and orientation.
///
/// Width is handled by SCALING rather than by splitting into left/right page strips. That is not a
/// shortcut around horizontal pagination so much as what the corpus asks for: 71 of its 77 sheets
/// set <c>fitToPage</c>, so Excel itself shrinks them to the page rather than splitting. Sheets that
/// are wide and do NOT ask to be fitted are scaled too, which is the one place this diverges from
/// Excel — it under-sizes them instead of paginating sideways.
/// </summary>
sealed class SpreadsheetParser(string defaultFont)
{
    const double pointsPerInch = 72.0;

    readonly Dictionary<OpenXmlPart, byte[]> partBytes = [];

    public SpreadsheetParser()
        : this(DefaultFontSettings.DefaultFont)
    {
    }

    public ParsedDocument Parse(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Parse(stream);
    }

    public ParsedDocument Parse(Stream stream)
    {
        var normalized = StrictToTransitional.Normalize(stream);
        try
        {
            using var document = SpreadsheetDocument.Open(normalized, false);
            return ParseWorkbook(document);
        }
        finally
        {
            if (!ReferenceEquals(normalized, stream))
            {
                normalized.Dispose();
            }
        }
    }

    ParsedDocument ParseWorkbook(SpreadsheetDocument document)
    {
        var workbookPart = document.WorkbookPart ??
                           throw new InvalidOperationException("The package has no workbook part.");

        var themeColors = ThemeParser.ExtractThemeColors(workbookPart.ThemePart);
        var themeFonts = ThemeParser.ExtractThemeFonts(workbookPart.ThemePart);
        var styles = new CellStyles(workbookPart, themeColors);
        var builder = new SheetGridBuilder(styles, new(workbookPart), defaultFont);
        var drawings = new SheetDrawingParser(
            themeColors,
            new(themeColors, themeFonts, defaultFont),
            GetPartBytes);
        var definedNames = DefinedNames.For(workbookPart);

        var elements = new List<DocumentElement>();
        PageSettings? first = null;

        foreach (var sheet in VisibleSheets(workbookPart))
        {
            if (workbookPart.GetPartById(sheet.Id!.Value!) is not WorksheetPart worksheetPart)
            {
                continue;
            }

            var worksheet = worksheetPart.Worksheet;
            if (worksheet == null)
            {
                continue;
            }

            var name = sheet.Name?.Value ?? string.Empty;
            var settings = PageSettingsFor(worksheet);

            var range = ResolveRange(worksheet, definedNames.PrintArea(name));
            if (range is not { } bounds || bounds.IsEmpty)
            {
                continue;
            }

            var scale = ResolveScale(worksheet, bounds, settings);
            var table = builder.Build(worksheet, bounds, scale, definedNames.PrintTitleRows(name));
            if (table == null)
            {
                continue;
            }

            // Drawings are emitted BEFORE the table: they anchor vertically to the flow cursor, which
            // still sits at the content top until the table is placed, so each binds to this sheet's
            // first page rather than deferring. Their coordinates are relative to the grid's
            // top-left, which is where the table begins.
            var art = drawings.Parse(worksheetPart, new(worksheet, bounds, scale), scale, 0);

            if (first == null)
            {
                first = settings;
            }
            else
            {
                // Every sheet after the first starts a page and adopts its own geometry — a workbook
                // routinely mixes portrait and landscape between sheets.
                elements.Add(new SectionBreakElement
                {
                    BreakType = SectionBreakType.NextPage,
                    NewSectionSettings = settings
                });
            }

            elements.AddRange(art);
            elements.Add(table);
        }

        return new()
        {
            PageSettings = first ?? new(),
            Elements = elements,
            ThemeColors = themeColors,
            ThemeFonts = themeFonts
        };
    }

    // One buffer per image part per parse, so a logo repeated across sheets shares a single array —
    // the render-side image caches key on byte-array reference identity.
    byte[] GetPartBytes(OpenXmlPart part)
    {
        if (partBytes.TryGetValue(part, out var cached))
        {
            return cached;
        }

        using var stream = part.GetStream();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        partBytes[part] = bytes;
        return bytes;
    }

    /// <summary>
    /// Sheets in tab order, skipping hidden ones. A hidden sheet is not printed, so rendering it
    /// would add pages Excel never produces.
    /// </summary>
    static IEnumerable<S.Sheet> VisibleSheets(WorkbookPart workbookPart) =>
        workbookPart.Workbook?.Sheets?
            .Elements<S.Sheet>()
            .Where(_ => _.State?.Value == null || _.State.Value == S.SheetStateValues.Visible)
            .Where(_ => _.Id?.Value != null) ?? [];

    /// <summary>
    /// The range to render: the sheet's declared print area when it has one, clipped to the cells
    /// that actually exist, else the used range from <c>dimension</c>.
    /// </summary>
    static SheetRange? ResolveRange(S.Worksheet worksheet, string? printArea)
    {
        var used = CellReference.ParseRange(worksheet.SheetDimension?.Reference?.Value) ?? UsedRange(worksheet);
        if (used is not { } bounds)
        {
            return null;
        }

        if (CellReference.ParseRange(printArea) is { } area)
        {
            var clipped = area.Intersect(bounds);
            return clipped.IsEmpty ? area : clipped;
        }

        return ExtendForOverflow(worksheet, bounds);
    }

    /// <summary>
    /// Widens a range to the columns the author shaped, when the last used column holds text that
    /// would otherwise be clipped at its edge.
    ///
    /// Word-style probe (<c>_probe_overflow</c>): a sheet whose <c>dimension</c> is column A alone,
    /// holding long unwrapped text, prints that text running across B through H and onto a SECOND
    /// page — so Excel's overflow is bounded by neither the column nor the used range, only by the
    /// next filled cell. Measured rightmost ink per row on the A4 reference: 115px for text that
    /// fits, 221px for text crossing one column, 672px plus a page-two continuation for text
    /// crossing seven, and a hard stop at the filled cell in C.
    ///
    /// Clipping at the used range therefore renders such a sheet blank. The range is extended to the
    /// last column carrying an explicit <c>customWidth</c> — the extent the author laid out, and a
    /// far closer bound than the used range. It is not the true rule, which would follow the text's
    /// measured width and can run off the page; that needs horizontal pagination, which this parser
    /// does not do.
    /// </summary>
    static SheetRange ExtendForOverflow(S.Worksheet worksheet, SheetRange bounds)
    {
        if (!HasOverflowingText(worksheet, bounds))
        {
            return bounds;
        }

        var shaped = worksheet.GetFirstChild<S.Columns>()?
            .Elements<S.Column>()
            .Where(_ => _.CustomWidth?.Value == true && _.Max?.Value is { } max && max < maxShapedColumn)
            .Select(_ => (int) _.Max!.Value)
            .DefaultIfEmpty(0)
            .Max() ?? 0;

        return shaped > bounds.LastColumn
            ? bounds with { LastColumn = shaped }
            : bounds;
    }

    /// <summary>
    /// A <c>col</c> run reaching the sheet's last column is the default-width catch-all every
    /// workbook ends with, not a shaped column; extending to it would widen a sheet by 16000 columns.
    /// </summary>
    const uint maxShapedColumn = 1000;

    /// <summary>Whether the range's final column holds text that would be clipped at its edge.</summary>
    static bool HasOverflowingText(S.Worksheet worksheet, SheetRange bounds) =>
        worksheet.GetFirstChild<S.SheetData>()?
            .Elements<S.Row>()
            .SelectMany(_ => _.Elements<S.Cell>())
            .Any(_ => CellReference.ColumnOf(_.CellReference?.Value) == bounds.LastColumn &&
                      _.CellValue?.Text is { Length: > 0 }) ?? false;

    /// <summary>The extent of the cells present, for a sheet whose <c>dimension</c> is missing.</summary>
    static SheetRange? UsedRange(S.Worksheet worksheet)
    {
        var sheetData = worksheet.GetFirstChild<S.SheetData>();
        if (sheetData == null)
        {
            return null;
        }

        int firstRow = int.MaxValue, lastRow = 0, firstColumn = int.MaxValue, lastColumn = 0;
        foreach (var row in sheetData.Elements<S.Row>())
        {
            foreach (var cell in row.Elements<S.Cell>())
            {
                var column = CellReference.ColumnOf(cell.CellReference?.Value);
                var index = CellReference.RowOf(cell.CellReference?.Value);
                if (column == 0 || index == 0)
                {
                    continue;
                }

                firstRow = Math.Min(firstRow, index);
                lastRow = Math.Max(lastRow, index);
                firstColumn = Math.Min(firstColumn, column);
                lastColumn = Math.Max(lastColumn, column);
            }
        }

        return lastRow == 0 ? null : new SheetRange(firstRow, firstColumn, lastRow, lastColumn);
    }

    /// <summary>
    /// The factor the sheet is drawn at. An explicit <c>scale</c> is honoured as written; otherwise
    /// the grid is shrunk until it fits the page width. Never enlarges — Excel's fit-to-page only
    /// shrinks.
    /// </summary>
    static double ResolveScale(S.Worksheet worksheet, SheetRange range, PageSettings settings)
    {
        var setup = worksheet.GetFirstChild<S.PageSetup>();
        var fitToPage = worksheet.SheetProperties?.PageSetupProperties?.FitToPage?.Value == true;

        if (!fitToPage && setup?.Scale?.Value is { } explicitScale && explicitScale != 100)
        {
            return explicitScale / 100.0;
        }

        var pagesWide = fitToPage ? Math.Max(1, setup?.FitToWidth?.Value ?? 1) : 1;
        var natural = SheetGridBuilder.NaturalWidthPoints(worksheet, range);
        var available = settings.ContentWidth * pagesWide;

        return natural > available && natural > 0 ? available / natural : 1;
    }

    static PageSettings PageSettingsFor(S.Worksheet worksheet)
    {
        var setup = worksheet.GetFirstChild<S.PageSetup>();
        var margins = worksheet.GetFirstChild<S.PageMargins>();

        var (width, height) = PaperSize.Resolve(setup?.PaperSize?.Value);
        var landscape = setup?.Orientation?.Value == S.OrientationValues.Landscape;

        return new()
        {
            WidthPoints = landscape ? height : width,
            HeightPoints = landscape ? width : height,
            // Excel's margins are inches.
            MarginLeft = (margins?.Left?.Value ?? 0.7) * pointsPerInch,
            MarginRight = (margins?.Right?.Value ?? 0.7) * pointsPerInch,
            MarginTop = (margins?.Top?.Value ?? 0.75) * pointsPerInch,
            MarginBottom = (margins?.Bottom?.Value ?? 0.75) * pointsPerInch,
            HeaderDistance = (margins?.Header?.Value ?? 0.3) * pointsPerInch,
            FooterDistance = (margins?.Footer?.Value ?? 0.3) * pointsPerInch
        };
    }

}
