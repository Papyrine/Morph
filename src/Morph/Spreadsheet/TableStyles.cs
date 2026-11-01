using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// The table styles of a sheet's tables (<c>xl/tables/table*.xml</c>, ECMA-376 §18.5): the banding,
/// header row, first and last columns a table draws from its <c>tableStyleInfo</c>.
///
/// A table style is a list of elements (§18.8.40), each a <c>dxf</c> laid over one region of the
/// table — the whole table, a row or column stripe, the header or total row, the first or last
/// column, a corner cell — and they compose lowest first, in the order <see cref="precedence"/> lists,
/// each element overriding the ones under it only in what its dxf says. A border element's
/// <c>left</c>/<c>right</c>/<c>top</c>/<c>bottom</c> are its region's outer edges and
/// <c>vertical</c>/<c>horizontal</c> the edges inside it, and a line between two cells is the one
/// the highest element on either side of it states; an edge an element STATES with no style
/// clears the one under it, which is how simple-basic-pink-blue-timesheet's header row (<c>&lt;top/&gt;</c>)
/// takes off the thin rule its whole-table element draws along the table's top in Excel's print.
///
/// The whole of it sits under the cell's own formatting, as Excel draws it: a visible border edge, a
/// fill and a font colour win where they are the cell's own rather than the Normal style's — a
/// workbook whose Normal style fills white (back-to-school-planner) still prints its table's stripes.
///
/// A workbook writes out only the styles it defines in its own <c>tableStyles</c>; Excel's built-in
/// styles are named and not written. 78 of the 80 corpus tables name a style the workbook defines and
/// the other two Light 1 and Light 7, which <see cref="BuiltIn"/> carries.
/// </summary>
sealed class TableStyles
{
    /// <summary>The order elements compose in, lowest first (§18.18.84 ST_TableStyleType).</summary>
    static readonly string[] precedence =
    [
        "wholeTable",
        "firstColumnStripe",
        "secondColumnStripe",
        "firstRowStripe",
        "secondRowStripe",
        "lastColumn",
        "firstColumn",
        "headerRow",
        "totalRow",
        "firstHeaderCell",
        "lastHeaderCell",
        "firstTotalCell",
        "lastTotalCell",
        // The table's own borders (tableBorderDxfId and its kin), which stand over its style:
        // simple-blue-timesheet-invoice's medium rule under its last row is its table border's
        "tableBorder",
        "headerRowBorder",
        "totalsRowBorder"
    ];

    readonly List<Table> tables = [];
    readonly CellStyles styles;

    public TableStyles(WorksheetPart? worksheetPart, S.Stylesheet? stylesheet, CellStyles styles)
    {
        this.styles = styles;
        if (worksheetPart == null)
        {
            return;
        }

        var formats = stylesheet?.DifferentialFormats?.Elements<S.DifferentialFormat>().ToList() ?? [];
        foreach (var part in worksheetPart.TableDefinitionParts)
        {
            if (part.Table is not { } definition ||
                CellReference.ParseRange(definition.Reference?.Value) is not { } range ||
                definition.TableStyleInfo is not {Name.Value: { } name} info ||
                (Defined(stylesheet, formats, name) ?? BuiltIn(name)) is not { } elements)
            {
                continue;
            }

            foreach (var (type, id) in new[]
                     {
                         ("tableBorder", definition.BorderFormatId?.Value),
                         ("headerRowBorder", definition.HeaderRowBorderFormatId?.Value),
                         ("totalsRowBorder", definition.TotalsRowBorderFormatId?.Value)
                     })
            {
                if (id is { } index &&
                    index < formats.Count &&
                    formats[(int) index].Border is { } border)
                {
                    elements[type] = (new() {Border = (S.Border) border.CloneNode(true)}, 1);
                }
            }

            tables.Add(new(
                range,
                (int) (definition.HeaderRowCount?.Value ?? 1),
                (int) (definition.TotalsRowCount?.Value ?? 0),
                elements,
                info.ShowFirstColumn?.Value == true,
                info.ShowLastColumn?.Value == true,
                info.ShowRowStripes?.Value == true,
                info.ShowColumnStripes?.Value == true));
        }
    }

    /// <summary>The elements of a style the workbook defines in its own <c>tableStyles</c>, or null when it defines none of that name.</summary>
    static Dictionary<string, (S.DifferentialFormat Format, int Size)>? Defined(S.Stylesheet? stylesheet, List<S.DifferentialFormat> formats, string name)
    {
        if (stylesheet?.TableStyles?.Elements<S.TableStyle>().FirstOrDefault(_ => _.Name?.Value == name) is not { } style)
        {
            return null;
        }

        var elements = new Dictionary<string, (S.DifferentialFormat Format, int Size)>(StringComparer.Ordinal);
        foreach (var element in style.Elements<S.TableStyleElement>())
        {
            if (element.Type?.Value is { } type &&
                element.FormatId?.Value is { } id &&
                id < formats.Count)
            {
                elements[((IEnumValue) type).Value] = (formats[(int) id], (int) (element.Size?.Value ?? 1));
            }
        }

        return elements;
    }

    /// <summary>
    /// Excel's built-in Light 1 to Light 7, which a workbook names without writing out. They were read
    /// from Excel itself (2026-11-01): <c>TableStyle.Duplicate</c> over COM writes a built-in style into
    /// the saved workbook as a style of its own. The seven are one design in seven colours: Light 1 in
    /// the text colour (theme 1) over the background darkened 15%, Light 2 to 7 in accents 1 to 6 with
    /// text darkened 25% and stripes lightened 80%. Every other built-in draws plain until it is read.
    /// </summary>
    static Dictionary<string, (S.DifferentialFormat Format, int Size)>? BuiltIn(string name)
    {
        if (!name.StartsWith("TableStyleLight", StringComparison.Ordinal) ||
            !int.TryParse(name.AsSpan("TableStyleLight".Length), out var number) ||
            number is < 1 or > 7)
        {
            return null;
        }

        var (text, rule, stripe) = number == 1
            ? ("theme=\"1\"", "theme=\"1\"", "theme=\"0\" tint=\"-0.14999847407452621\"")
            : ($"theme=\"{number + 2}\" tint=\"-0.249977111117893\"", $"theme=\"{number + 2}\"", $"theme=\"{number + 2}\" tint=\"0.79998168889431442\"");

        S.DifferentialFormat Format(string inner) =>
            new($"<x:dxf xmlns:x=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">{inner}</x:dxf>");

        string Border(params string[] sides) =>
            $"<x:border>{string.Concat(sides.Select(_ => $"<x:{_} style=\"thin\"><x:color {rule}/></x:{_}>"))}</x:border>";

        var bold = $"<x:font><x:b/><x:color {text}/></x:font>";
        var fill = $"<x:fill><x:patternFill patternType=\"solid\"><x:fgColor {stripe}/><x:bgColor {stripe}/></x:patternFill></x:fill>";
        return new(StringComparer.Ordinal)
        {
            ["wholeTable"] = (Format($"<x:font><x:color {text}/></x:font>{Border("top", "bottom")}"), 1),
            ["headerRow"] = (Format(bold + Border("bottom")), 1),
            ["totalRow"] = (Format(bold + Border("top")), 1),
            ["firstColumn"] = (Format(bold), 1),
            ["lastColumn"] = (Format(bold), 1),
            ["firstRowStripe"] = (Format(fill), 1),
            ["firstColumnStripe"] = (Format(fill), 1)
        };
    }

    public bool IsEmpty => tables.Count == 0;

    /// <summary>What the table a cell stands in draws on it, or null outside every table.</summary>
    public TableStyleOverlay? For(int row, int column)
    {
        foreach (var table in tables)
        {
            if (row >= table.Range.FirstRow &&
                row <= table.Range.LastRow &&
                column >= table.Range.FirstColumn &&
                column <= table.Range.LastColumn)
            {
                return Overlay(table, row, column);
            }
        }

        return null;
    }

    TableStyleOverlay Overlay(Table table, int row, int column)
    {
        var overlay = new TableStyleOverlay();
        foreach (var type in precedence)
        {
            if (table.Elements.TryGetValue(type, out var element) &&
                Region(table, type, row, column) is not null)
            {
                overlay = Apply(overlay, element.Format);
            }
        }

        return overlay with
        {
            Left = Boundary(table, row, column, 0, -1),
            Right = Boundary(table, row, column, 0, 1),
            Top = Boundary(table, row, column, -1, 0),
            Bottom = Boundary(table, row, column, 1, 0)
        };
    }

    /// <summary>
    /// The line between a cell and its neighbour one step away, as the elements on either side
    /// state it, the highest in precedence winning: simple-list's and back-to-school-planner's
    /// header rows state a thick bottom, which stands over the first data row's top where the
    /// whole table states a thin inside line.
    /// </summary>
    BorderEdge? Boundary(Table table, int row, int column, int rowStep, int columnStep)
    {
        var neighbourRow = row + rowStep;
        var neighbourColumn = column + columnStep;
        var neighbourInside = neighbourRow >= table.Range.FirstRow &&
                              neighbourRow <= table.Range.LastRow &&
                              neighbourColumn >= table.Range.FirstColumn &&
                              neighbourColumn <= table.Range.LastColumn;

        BorderEdge? result = null;
        foreach (var type in precedence)
        {
            if (!table.Elements.TryGetValue(type, out var element) ||
                element.Format.Border is not { } border)
            {
                continue;
            }

            var stated = Region(table, type, row, column) is { } own
                ? Side(border, own, row, column, rowStep, columnStep)
                : null;
            if (stated == null &&
                neighbourInside &&
                Region(table, type, neighbourRow, neighbourColumn) is { } beside)
            {
                stated = Side(border, beside, neighbourRow, neighbourColumn, -rowStep, -columnStep);
            }

            result = stated ?? result;
        }

        return result;
    }

    /// <summary>The edge a region's element states on one side of a cell in it: an outer edge of the region or an inside one.</summary>
    BorderEdge? Side(S.Border border, SheetRange region, int row, int column, int rowStep, int columnStep) =>
        Edge((rowStep, columnStep) switch
        {
            (0, -1) => column == region.FirstColumn ? border.LeftBorder : border.VerticalBorder,
            (0, 1) => column == region.LastColumn ? border.RightBorder : border.VerticalBorder,
            (-1, 0) => row == region.FirstRow ? border.TopBorder : border.HorizontalBorder,
            _ => row == region.LastRow ? border.BottomBorder : border.HorizontalBorder
        });

    /// <summary>The region an element covers around the cell, or null when it does not cover the cell.</summary>
    static SheetRange? Region(Table table, string type, int row, int column)
    {
        var range = table.Range;
        var firstData = range.FirstRow + table.HeaderRows;
        var lastData = range.LastRow - table.TotalRows;
        var inHeader = row < firstData;
        var inTotal = row > lastData;
        var inData = !inHeader && !inTotal;
        var isFirst = column == range.FirstColumn;
        var isLast = column == range.LastColumn;
        return type switch
        {
            "wholeTable" => range,
            "firstColumnStripe" or "secondColumnStripe" when table.ColumnStripes && inData =>
                Stripe(table, column - range.FirstColumn, "firstColumnStripe", "secondColumnStripe", type) is { } band
                    ? new(firstData, range.FirstColumn + band.Start, lastData, range.FirstColumn + band.End)
                    : null,
            "firstRowStripe" or "secondRowStripe" when table.RowStripes && inData =>
                Stripe(table, row - firstData, "firstRowStripe", "secondRowStripe", type) is { } band
                    ? new(firstData + band.Start, range.FirstColumn, firstData + band.End, range.LastColumn)
                    : null,
            "lastColumn" when table.LastColumn && isLast => range with {FirstColumn = column},
            "firstColumn" when table.FirstColumn && isFirst => range with {LastColumn = column},
            "headerRow" when inHeader => range with {LastRow = firstData - 1},
            "totalRow" when inTotal => range with {FirstRow = lastData + 1},
            "firstHeaderCell" when inHeader && isFirst && table.FirstColumn => new(range.FirstRow, column, firstData - 1, column),
            "lastHeaderCell" when inHeader && isLast && table.LastColumn => new(range.FirstRow, column, firstData - 1, column),
            "firstTotalCell" when inTotal && isFirst && table.FirstColumn => new(lastData + 1, column, range.LastRow, column),
            "lastTotalCell" when inTotal && isLast && table.LastColumn => new(lastData + 1, column, range.LastRow, column),
            "tableBorder" => range,
            "headerRowBorder" when inHeader => range with {LastRow = firstData - 1},
            "totalsRowBorder" when inTotal => range with {FirstRow = lastData + 1},
            _ => null
        };
    }

    /// <summary>
    /// The band of stripes the offset falls in, as offsets from the first data row or column, when
    /// it is the band <paramref name="type"/> names. The first and second stripes alternate, each as
    /// many rows or columns deep as its element's <c>size</c>.
    /// </summary>
    static (int Start, int End)? Stripe(Table table, int offset, string first, string second, string type)
    {
        var firstSize = table.Elements.TryGetValue(first, out var one) ? Math.Max(1, one.Size) : 1;
        var secondSize = table.Elements.TryGetValue(second, out var two) ? Math.Max(1, two.Size) : 1;
        var position = offset % (firstSize + secondSize);
        var start = offset - position;
        if (position < firstSize)
        {
            return type == first ? (start, start + firstSize - 1) : null;
        }

        return type == second ? (start + firstSize, start + firstSize + secondSize - 1) : null;
    }

    TableStyleOverlay Apply(TableStyleOverlay overlay, S.DifferentialFormat format)
    {
        if (format.Fill?.PatternFill is { } fill)
        {
            // A dxf's solid fill states its colour in bgColor, as a conditional format's does
            overlay = fill.PatternType?.Value == S.PatternValues.None
                ? overlay with {BackgroundColorHex = null}
                : overlay with {BackgroundColorHex = styles.Resolve(fill.BackgroundColor) ?? styles.Resolve(fill.ForegroundColor) ?? overlay.BackgroundColorHex};
        }

        if (format.Font is { } font)
        {
            overlay = overlay with
            {
                ColorHex = styles.Resolve(font.Color) ?? overlay.ColorHex,
                Bold = font.Bold is { } bold ? bold.Val?.Value ?? true : overlay.Bold,
                Italic = font.Italic is { } italic ? italic.Val?.Value ?? true : overlay.Italic
            };
        }

        return overlay;
    }

    /// <summary>The edge an element states, None when it states one with no style, null when it states none.</summary>
    BorderEdge? Edge(S.BorderPropertiesType? edge) =>
        edge == null ? null : styles.Edge(edge);

    sealed record Table(
        SheetRange Range,
        int HeaderRows,
        int TotalRows,
        Dictionary<string, (S.DifferentialFormat Format, int Size)> Elements,
        bool FirstColumn,
        bool LastColumn,
        bool RowStripes,
        bool ColumnStripes);
}

/// <summary>
/// What a table's style draws on one of its cells. A null member is one no element of the style
/// states, which leaves the cell's own.
/// </summary>
readonly record struct TableStyleOverlay
{
    public string? BackgroundColorHex { get; init; }
    public string? ColorHex { get; init; }
    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
    public BorderEdge? Left { get; init; }
    public BorderEdge? Right { get; init; }
    public BorderEdge? Top { get; init; }
    public BorderEdge? Bottom { get; init; }
}
