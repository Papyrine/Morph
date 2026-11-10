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
    readonly string? backdropHex;

    TableStyleResolver(OpenXmlElement? style, ThemeColors? themeColors, string? backdropHex)
    {
        this.style = style;
        this.themeColors = themeColors;
        this.backdropHex = backdropHex;
    }

    /// <summary>
    /// The style a table references, or nothing when it references none.
    ///
    /// A table without an <c>a:tableStyleId</c> gets NO style — its cells carry their own borders and
    /// fills. The <c>def</c> attribute on <c>a:tblStyleLst</c> is the style PowerPoint gives a newly
    /// inserted table, not a fallback for tables that omit the id; treating it as one paints banding
    /// and header fills over decks that deliberately have neither.
    ///
    /// <paramref name="backdropHex"/> is the slide's background colour, which a translucent style fill
    /// is blended over (see <see cref="Resolve"/>); null blends over white.
    /// </summary>
    public static TableStyleResolver For(A.Table table, TableStylesPart? stylesPart, ThemeColors? themeColors, string? backdropHex = null)
    {
        var root = stylesPart?.RootElement;
        var wanted = table.TableProperties?.GetFirstChild<A.TableStyleId>()?.Text;
        if (root == null || string.IsNullOrEmpty(wanted))
        {
            return new(null, themeColors, backdropHex);
        }

        var match = root.Elements()
            .Where(_ => _.LocalName == "tblStyle")
            .FirstOrDefault(_ => _.GetAttributes().FirstOrDefault(a => a.LocalName == "styleId").Value == wanted);

        return new(match, themeColors, backdropHex);
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
            if (bodyColumn >= 0 &&
                Part(bodyColumn % 2 == 0 ? "band1V" : "band2V") is { } bandedColumn)
            {
                yield return bandedColumn;
            }
        }

        if (firstColumn &&
            columnIndex == 0 &&
            Part("firstCol") is { } first)
        {
            yield return first;
        }

        if (lastColumn &&
            columnIndex == columnCount - 1 &&
            Part("lastCol") is { } last)
        {
            yield return last;
        }

        // The edge ROWS come last: a header row's fill wins over a first-column fill where they meet.
        if (lastRow &&
            rowIndex == rowCount - 1 &&
            Part("lastRow") is { } bottom)
        {
            yield return bottom;
        }

        if (firstRow &&
            rowIndex == 0 &&
            Part("firstRow") is { } top)
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
            if (fill != null &&
                ShapeParser.ExtractSolidFillColor(fill, themeColors) is { } background)
            {
                result = result with
                {
                    BackgroundColorHex = Composite(background, ShapeParser.ExtractSolidFillAlpha(fill))
                };
            }

            var borders = cellStyle?.Elements().FirstOrDefault(_ => _.LocalName == "tcBdr");
            if (borders != null)
            {
                // A part's left/right/top/bottom are the edges of the REGION it covers, and its
                // insideV/insideH the rules within it: wholeTbl's top is the table's top edge only.
                // Read as every cell's own edges, Light Style 1's top and bottom rules drew between
                // every pair of rows, where PowerPoint draws them only around the table and under
                // the header (Basic.pptx's metrics tables).
                var region = Region(part.LocalName, rowIndex, rowCount, columnIndex, columnCount);
                result = result with
                {
                    Left = Edge(borders, columnIndex == region.FirstColumn ? "left" : "insideV") ?? result.Left,
                    Right = Edge(borders, columnIndex == region.LastColumn ? "right" : "insideV") ?? result.Right,
                    Top = Edge(borders, rowIndex == region.FirstRow ? "top" : "insideH") ?? result.Top,
                    Bottom = Edge(borders, rowIndex == region.LastRow ? "bottom" : "insideH") ?? result.Bottom
                };
            }

            var textStyle = part.Elements().FirstOrDefault(_ => _.LocalName == "tcTxStyle");
            if (textStyle != null)
            {
                if (ShapeParser.ExtractSolidFillColor(textStyle, themeColors) is { } color)
                {
                    result = result with
                    {
                        ColorHex = color
                    };
                }

                // b/i are tri-state here ("on"/"off"/"def"); only an explicit "on" forces the weight.
                var bold = textStyle.GetAttributes().FirstOrDefault(_ => _.LocalName == "b").Value;
                if (bold is "on" or "1")
                {
                    result = result with
                    {
                        Bold = true
                    };
                }
            }
        }

        return result;
    }

    /// <summary>
    /// A style fill with an <c>a:alpha</c>, laid over what is behind the table. The Light styles band
    /// their rows in their accent at 20% (Basic.pptx's two metrics tables), which drawn opaque turned
    /// a pale wash into full-strength accent stripes. A cell's shading has no alpha in the model, so
    /// the blend is made here, over the slide's background — what a slide table stands on.
    /// </summary>
    string Composite(string colorHex, double alpha)
    {
        if (alpha >= 1 ||
            colorHex.Length != 6)
        {
            return colorHex;
        }

        var backdrop = backdropHex is { Length: 6 } ? backdropHex : "FFFFFF";

        int Channel(int offset) =>
            (int) Math.Round(
                int.Parse(colorHex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) * alpha +
                int.Parse(backdrop.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) * (1 - alpha));

        return $"{Channel(0):X2}{Channel(2):X2}{Channel(4):X2}";
    }

    OpenXmlElement? Part(string name) =>
        style?.Elements().FirstOrDefault(_ => _.LocalName == name);

    /// <summary>
    /// The rows and columns a style part covers, for the cell at (<paramref name="rowIndex"/>,
    /// <paramref name="columnIndex"/>): a row part (the header, the total row, a row band) spans its
    /// one row across the table, a column part its one column down it, and wholeTbl everything.
    /// </summary>
    static (int FirstRow, int LastRow, int FirstColumn, int LastColumn) Region(string part, int rowIndex, int rowCount, int columnIndex, int columnCount) =>
        part switch
        {
            "firstRow" or "lastRow" or "band1H" or "band2H" => (rowIndex, rowIndex, 0, columnCount - 1),
            "firstCol" or "lastCol" or "band1V" or "band2V" => (0, rowCount - 1, columnIndex, columnIndex),
            _ => (0, rowCount - 1, 0, columnCount - 1)
        };

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
