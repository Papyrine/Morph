using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// Converts a slide's <c>a:tbl</c> into the shared table model.
///
/// The grid maps over almost directly — <c>a:gridCol/@w</c> and <c>a:tr/@h</c> are EMU where the
/// model wants points, and merges are the same two concepts under different names. The work is in
/// the formatting, which a slide table barely declares: the deck picks a table style and the cells
/// inherit fills, borders and text colour from it (see <see cref="TableStyleResolver"/>), with a
/// cell's own <c>a:tcPr</c> overriding what the style supplies.
/// </summary>
sealed class SlideTableParser(ThemeColors? themeColors, DrawingTextParser textParser)
{
    /// <summary>PowerPoint's default cell insets (ECMA-376 §21.1.3.17): 0.1" sides, 0.05" ends.</summary>
    const double defaultSideMarginEmu = 91440;

    const double defaultEndMarginEmu = 45720;

    public TableElement? Parse(A.Table table, TableStylesPart? stylesPart)
    {
        var rows = table.Elements<A.TableRow>().ToArray();
        if (rows.Length == 0)
        {
            return null;
        }

        var style = TableStyleResolver.For(table, stylesPart, themeColors);
        var properties = table.TableProperties;

        var columnWidths = table.TableGrid?.Elements<A.GridColumn>()
            .Select(_ => (_.Width?.Value ?? 0) / OoxmlUnits.EmusPerPoint)
            .ToArray() ?? [];

        var parsedRows = new List<TableRow>(rows.Length);
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            parsedRows.Add(ParseRow(rows[rowIndex], style, properties, rowIndex, rows.Length, columnWidths));
        }

        return new()
        {
            Rows = parsedRows,
            Properties = new()
            {
                GridColumnWidths = columnWidths.Length > 0 ? columnWidths : null,
                // The grid is authoritative and every cell declares its own borders, so the table
                // contributes no default border of its own.
                IsAutoFit = false
            }
        };
    }

    TableRow ParseRow(
        A.TableRow row,
        TableStyleResolver style,
        A.TableProperties? properties,
        int rowIndex,
        int rowCount,
        double[] columnWidths)
    {
        var cells = row.Elements<A.TableCell>().ToArray();
        var parsed = new List<TableCell>(cells.Length);

        for (var columnIndex = 0; columnIndex < cells.Length; columnIndex++)
        {
            parsed.Add(ParseCell(
                cells[columnIndex],
                style.Resolve(properties, rowIndex, rowCount, columnIndex, cells.Length),
                columnIndex < columnWidths.Length ? columnWidths[columnIndex] : null));
        }

        return new()
        {
            Cells = parsed,
            // a:tr/@h is a MINIMUM in PowerPoint — a row grows to fit its text — so it is never
            // pinned exact, which would clip.
            HeightPoints = (row.Height?.Value ?? 0) / OoxmlUnits.EmusPerPoint,
            IsExactHeight = false
        };
    }

    TableCell ParseCell(A.TableCell cell, TableStyleCell styled, double? widthPoints)
    {
        var properties = cell.TableCellProperties;

        // The cell's own fill wins over the style's; a:noFill means transparent, not "inherit".
        var ownFill = properties?.GetFirstChild<A.SolidFill>();
        var background = ownFill != null
            ? ShapeParser.ExtractSolidFillColor(ownFill, themeColors)
            : properties?.GetFirstChild<A.NoFill>() != null
                ? null
                : styled.BackgroundColorHex;

        var content = cell.TextBody is { } body
            ? textParser.Parse(body, new([]))
            : [];

        return new()
        {
            Content = ApplyTextStyle(content, styled),
            Properties = new()
            {
                WidthPoints = widthPoints,
                BackgroundColorHex = background,
                Padding = new(
                    (properties?.TopMargin?.Value ?? defaultEndMarginEmu) / OoxmlUnits.EmusPerPoint,
                    (properties?.RightMargin?.Value ?? defaultSideMarginEmu) / OoxmlUnits.EmusPerPoint,
                    (properties?.BottomMargin?.Value ?? defaultEndMarginEmu) / OoxmlUnits.EmusPerPoint,
                    (properties?.LeftMargin?.Value ?? defaultSideMarginEmu) / OoxmlUnits.EmusPerPoint),
                Borders = ResolveBorders(properties, styled),
                GridSpan = cell.GridSpan?.Value ?? 1,
                VerticalMerge = cell.VerticalMerge?.Value == true
                    ? VerticalMergeType.Continue
                    : cell.RowSpan?.Value > 1
                        ? VerticalMergeType.Restart
                        : VerticalMergeType.None,
                VerticalAlignment = MapAnchor(properties?.Anchor?.Value)
            }
        };
    }

    CellBorders ResolveBorders(A.TableCellProperties? properties, TableStyleCell styled) =>
        new()
        {
            Left = Edge(properties?.GetFirstChild<A.LeftBorderLineProperties>()) ?? styled.Left ?? BorderEdge.None,
            Right = Edge(properties?.GetFirstChild<A.RightBorderLineProperties>()) ?? styled.Right ?? BorderEdge.None,
            Top = Edge(properties?.GetFirstChild<A.TopBorderLineProperties>()) ?? styled.Top ?? BorderEdge.None,
            Bottom = Edge(properties?.GetFirstChild<A.BottomBorderLineProperties>()) ?? styled.Bottom ?? BorderEdge.None
        };

    BorderEdge? Edge(OpenXmlElement? line)
    {
        if (line == null)
        {
            return null;
        }

        if (line.GetFirstChild<A.NoFill>() != null)
        {
            return BorderEdge.None;
        }

        var solid = line.GetFirstChild<A.SolidFill>();
        if (solid == null)
        {
            return null;
        }

        var width = line.GetAttributes().FirstOrDefault(_ => _.LocalName == "w").Value;
        return new()
        {
            IsVisible = true,
            WidthPoints = (long.TryParse(width, out var emu) ? emu : 12700) / 12700.0,
            ColorHex = ShapeParser.ExtractSolidFillColor(solid, themeColors) ?? "000000"
        };
    }

    /// <summary>
    /// Applies the table style's text treatment to runs that did not declare their own. A style
    /// supplies a default, so an explicit run colour or weight on the cell always wins.
    /// </summary>
    static List<DocumentElement> ApplyTextStyle(List<DocumentElement> content, TableStyleCell styled)
    {
        if (styled.ColorHex == null && !styled.Bold)
        {
            return content;
        }

        var result = new List<DocumentElement>(content.Count);
        foreach (var element in content)
        {
            if (element is not ParagraphElement paragraph)
            {
                result.Add(element);
                continue;
            }

            result.Add(
                new ParagraphElement
                {
                    Runs = paragraph.Runs
                        .Select(_ => _.WithProperties(
                            _.Properties with
                            {
                                ColorHex = _.Properties.ColorHex ?? styled.ColorHex,
                                Bold = _.Properties.Bold || styled.Bold
                            }))
                        .ToArray(),
                    Properties = paragraph.Properties
                });
        }

        return result;
    }

    static CellVerticalAlignment MapAnchor(A.TextAnchoringTypeValues? anchor)
    {
        if (anchor == A.TextAnchoringTypeValues.Center)
        {
            return CellVerticalAlignment.Center;
        }

        if (anchor == A.TextAnchoringTypeValues.Bottom)
        {
            return CellVerticalAlignment.Bottom;
        }

        return CellVerticalAlignment.Top;
    }
}
