using A = DocumentFormat.OpenXml.Drawing;

/// <summary>
/// Resolves a DrawingML table style (<c>ppt/tableStyles.xml</c>) for one cell.
///
/// A slide table carries very little direct formatting: the deck's designer picks a table style and
/// the cells inherit fills, borders and text colour from it. The style is a set of overlapping parts
/// — <c>a:wholeTbl</c> under everything, then banding, then the edge rows and columns — and a cell
/// takes the LAST part that declares each property, which is what the ordering in
/// <see cref="PartsFor"/> encodes.
///
/// Which parts are live is governed by <c>a:tblPr</c>: a table only gets first-row treatment if it
/// asked for it with <c>firstRow="1"</c>, and banding only with <c>bandRow="1"</c>.
/// </summary>
sealed class TableStyleResolver
{
    readonly OpenXmlElement? style;
    readonly ThemeColors? themeColors;

    TableStyleResolver(OpenXmlElement? style, ThemeColors? themeColors)
    {
        this.style = style;
        this.themeColors = themeColors;
    }

    /// <summary>
    /// The style a table references, or nothing when it references none.
    ///
    /// A table without an <c>a:tableStyleId</c> gets NO style — its cells carry their own borders and
    /// fills. The <c>def</c> attribute on <c>a:tblStyleLst</c> is the style PowerPoint gives a newly
    /// inserted table, not a fallback for tables that omit the id; treating it as one paints banding
    /// and header fills over decks that deliberately have neither.
    /// </summary>
    public static TableStyleResolver For(A.Table table, TableStylesPart? stylesPart, ThemeColors? themeColors)
    {
        var root = stylesPart?.RootElement;
        var wanted = table.TableProperties?.GetFirstChild<A.TableStyleId>()?.Text;
        if (root == null || string.IsNullOrEmpty(wanted))
        {
            return new(null, themeColors);
        }

        var match = root.Elements()
            .Where(_ => _.LocalName == "tblStyle")
            .FirstOrDefault(_ => _.GetAttributes().FirstOrDefault(a => a.LocalName == "styleId").Value == wanted);

        return new(match, themeColors);
    }

    /// <summary>
    /// The style parts that apply to a cell, least specific first, so a later part overrides an
    /// earlier one. Banding alternates in ONE-based row terms: the first body row is band 1.
    /// </summary>
    IEnumerable<OpenXmlElement> PartsFor(A.TableProperties? properties, int rowIndex, int rowCount, int columnIndex, int columnCount)
    {
        if (style == null)
        {
            yield break;
        }

        if (Part("wholeTbl") is { } whole)
        {
            yield return whole;
        }

        var firstRow = properties?.FirstRow?.Value == true;
        var lastRow = properties?.LastRow?.Value == true;
        var firstColumn = properties?.FirstColumn?.Value == true;
        var lastColumn = properties?.LastColumn?.Value == true;

        if (properties?.BandRow?.Value == true)
        {
            // Banding counts body rows only, so a header row shifts the stripes by one.
            var bodyIndex = rowIndex - (firstRow ? 1 : 0);
            if (bodyIndex >= 0)
            {
                var band = bodyIndex % 2 == 0 ? "band1H" : "band2H";
                if (Part(band) is { } banded)
                {
                    yield return banded;
                }
            }
        }

        if (properties?.BandColumn?.Value == true)
        {
            var bodyColumn = columnIndex - (firstColumn ? 1 : 0);
            if (bodyColumn >= 0 && Part(bodyColumn % 2 == 0 ? "band1V" : "band2V") is { } bandedColumn)
            {
                yield return bandedColumn;
            }
        }

        if (firstColumn && columnIndex == 0 && Part("firstCol") is { } first)
        {
            yield return first;
        }

        if (lastColumn && columnIndex == columnCount - 1 && Part("lastCol") is { } last)
        {
            yield return last;
        }

        // The edge ROWS come last: a header row's fill wins over a first-column fill where they meet.
        if (lastRow && rowIndex == rowCount - 1 && Part("lastRow") is { } bottom)
        {
            yield return bottom;
        }

        if (firstRow && rowIndex == 0 && Part("firstRow") is { } top)
        {
            yield return top;
        }
    }

    /// <summary>The cell's resolved fill, borders and text treatment from the style alone.</summary>
    public TableStyleCell Resolve(A.TableProperties? properties, int rowIndex, int rowCount, int columnIndex, int columnCount)
    {
        var result = new TableStyleCell();

        foreach (var part in PartsFor(properties, rowIndex, rowCount, columnIndex, columnCount))
        {
            var cellStyle = part.Elements().FirstOrDefault(_ => _.LocalName == "tcStyle");
            var fill = cellStyle?.Elements().FirstOrDefault(_ => _.LocalName == "fill")?
                .GetFirstChild<A.SolidFill>();
            if (fill != null && ShapeParser.ExtractSolidFillColor(fill, themeColors) is { } background)
            {
                result = result with { BackgroundColorHex = background };
            }

            var borders = cellStyle?.Elements().FirstOrDefault(_ => _.LocalName == "tcBdr");
            if (borders != null)
            {
                result = result with
                {
                    Left = Edge(borders, "left") ?? result.Left,
                    Right = Edge(borders, "right") ?? result.Right,
                    Top = Edge(borders, "top") ?? result.Top,
                    Bottom = Edge(borders, "bottom") ?? result.Bottom
                };
            }

            var textStyle = part.Elements().FirstOrDefault(_ => _.LocalName == "tcTxStyle");
            if (textStyle != null)
            {
                if (ShapeParser.ExtractSolidFillColor(textStyle, themeColors) is { } color)
                {
                    result = result with { ColorHex = color };
                }

                // b/i are tri-state here ("on"/"off"/"def"); only an explicit "on" forces the weight.
                var bold = textStyle.GetAttributes().FirstOrDefault(_ => _.LocalName == "b").Value;
                if (bold is "on" or "1")
                {
                    result = result with { Bold = true };
                }
            }
        }

        return result;
    }

    OpenXmlElement? Part(string name) =>
        style?.Elements().FirstOrDefault(_ => _.LocalName == name);

    BorderEdge? Edge(OpenXmlElement borders, string name)
    {
        var edge = borders.Elements().FirstOrDefault(_ => _.LocalName == name);
        var outline = edge?.GetFirstChild<A.Outline>();
        if (outline == null)
        {
            return null;
        }

        // An explicit a:noFill on the line is the style switching a border OFF, which has to be
        // recorded rather than skipped — it is how a banded style suppresses inner rules.
        if (outline.GetFirstChild<A.NoFill>() != null)
        {
            return BorderEdge.None;
        }

        var solid = outline.GetFirstChild<A.SolidFill>();
        if (solid == null)
        {
            return null;
        }

        return new()
        {
            IsVisible = true,
            WidthPoints = (outline.Width?.Value ?? 12700) / 12700.0,
            ColorHex = ShapeParser.ExtractSolidFillColor(solid, themeColors) ?? "000000"
        };
    }
}

/// <summary>What a table style contributes to one cell, before its own <c>a:tcPr</c> overrides it.</summary>
readonly record struct TableStyleCell
{
    public string? BackgroundColorHex { get; init; }
    public string? ColorHex { get; init; }
    public bool Bold { get; init; }
    public BorderEdge? Left { get; init; }
    public BorderEdge? Right { get; init; }
    public BorderEdge? Top { get; init; }
    public BorderEdge? Bottom { get; init; }
}
