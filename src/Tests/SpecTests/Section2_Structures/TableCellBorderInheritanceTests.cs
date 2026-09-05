using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using W = DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// A cell's <c>w:tcBorders</c> speaks only for the sides it names: the others keep the table-level
/// cascade (<see cref="CellBorders.Declared"/>), and only a side a neighbour DECLARES invisible
/// suppresses the rule they share. Word-read on business-plans/10, whose header cells declare a
/// bottom rule alone and whose first body row a top rule alone — Word keeps the full grid around them.
/// </summary>
public class TableCellBorderInheritanceTests
{
    [Test]
    public async Task A_cell_declaring_one_side_keeps_the_cascade_on_the_others()
    {
        var table = Grid("00FF00");
        var cell = new TableCellProperties
        {
            Borders = new()
            {
                Bottom = Edge("FF0000", 1.5),
                Declared = BorderSides.Bottom
            }
        };

        var borders = TableLayout.ResolveCellBorders(cell, table, rowIndex: 0, colIndex: 1, totalRows: 3, totalCols: 3);

        await Assert.That(borders).IsNotNull();
        await Assert.That(borders!.Bottom.ColorHex).IsEqualTo("FF0000");
        await Assert.That(borders.Bottom.WidthPoints).IsEqualTo(1.5);
        await Assert.That(borders.Top.ColorHex).IsEqualTo("0000FF");
        await Assert.That(borders.Left.ColorHex).IsEqualTo("00FF00");
        await Assert.That(borders.Right.ColorHex).IsEqualTo("00FF00");
        await Assert.That(borders.Declared).IsEqualTo(BorderSides.All);
    }

    [Test]
    public async Task A_cell_declaring_all_four_sides_is_taken_whole()
    {
        var table = Grid("00FF00");
        var cell = new TableCellProperties
        {
            Borders = new()
            {
                Top = BorderEdge.None,
                Right = BorderEdge.None,
                Bottom = Edge("FF0000", 1.5),
                Left = BorderEdge.None
            }
        };

        var borders = TableLayout.ResolveCellBorders(cell, table, rowIndex: 0, colIndex: 1, totalRows: 3, totalCols: 3);

        await Assert.That(borders!.Top.IsVisible).IsFalse();
        await Assert.That(borders.Left.IsVisible).IsFalse();
        await Assert.That(borders.Bottom.ColorHex).IsEqualTo("FF0000");
    }

    [Test]
    public async Task An_undeclared_side_on_the_neighbour_does_not_suppress_the_shared_rule()
    {
        var table = Grid("00FF00");
        var partial = new TableCellProperties
        {
            Borders = new()
            {
                Bottom = Edge("FF0000", 1.5),
                Declared = BorderSides.Bottom
            }
        };
        var plain = new TableCellProperties();
        var row = new TableRow
        {
            Cells =
            [
                new() { Properties = partial, Content = [] },
                new() { Properties = plain, Content = [] }
            ]
        };

        var borders = TableLayout.ResolveCellBorders(plain, table, rowIndex: 0, colIndex: 1, totalRows: 1, totalCols: 2, row, [row]);

        await Assert.That(borders!.Left.ColorHex).IsEqualTo("00FF00");
        await Assert.That(borders.Left.IsVisible).IsTrue();
    }

    [Test]
    public async Task A_declared_nil_on_the_neighbour_still_suppresses_the_shared_rule()
    {
        var table = Grid("00FF00");
        var nilled = new TableCellProperties
        {
            Borders = new()
            {
                Right = BorderEdge.None,
                Declared = BorderSides.Right
            }
        };
        var plain = new TableCellProperties();
        var row = new TableRow
        {
            Cells =
            [
                new() { Properties = nilled, Content = [] },
                new() { Properties = plain, Content = [] }
            ]
        };

        var borders = TableLayout.ResolveCellBorders(plain, table, rowIndex: 0, colIndex: 1, totalRows: 1, totalCols: 2, row, [row]);

        await Assert.That(borders!.Left.IsVisible).IsFalse();
    }

    [Test]
    public async Task Over_takes_declared_sides_from_the_upper_record_and_the_rest_from_the_lower()
    {
        var upper = new CellBorders { Top = Edge("111111", 3), Declared = BorderSides.Top };
        var lower = new CellBorders { Top = Edge("222222", 1), Left = Edge("333333", 1), Declared = BorderSides.Top | BorderSides.Left };

        var merged = upper.Over(lower);

        await Assert.That(merged.Top.ColorHex).IsEqualTo("111111");
        await Assert.That(merged.Left.ColorHex).IsEqualTo("333333");
        await Assert.That(merged.Declared).IsEqualTo(BorderSides.Top | BorderSides.Left);
        await Assert.That(upper.Over(null)).IsSameReferenceAs(upper);
    }

    [Test]
    public async Task The_parser_records_which_sides_a_tcBorders_declares()
    {
        using var stream = BuildDocument();
        var document = new DocumentParser().Parse(stream);
        var table = document.Elements.OfType<TableElement>().First();

        var header = table.Rows[0].Cells[0].Properties.Borders;
        await Assert.That(header).IsNotNull();
        await Assert.That(header!.Declared).IsEqualTo(BorderSides.Bottom);
        await Assert.That(header.Bottom.WidthPoints).IsEqualTo(1.5);

        var body = table.Rows[1].Cells[0].Properties.Borders;
        await Assert.That(body!.Declared).IsEqualTo(BorderSides.Top | BorderSides.Left);
        await Assert.That(body.Left.IsVisible).IsFalse();

        // Diagonals alone leave the side cascade untouched.
        await Assert.That(table.Rows[1].Cells[1].Properties.Borders).IsNull();
        await Assert.That(table.Rows[1].Cells[1].Properties.Diagonals).IsNotNull();
    }

    static TableProperties Grid(string inside) => new()
    {
        DefaultBorders = new()
        {
            Top = Edge("0000FF", 0.5),
            Right = Edge("0000FF", 0.5),
            Bottom = Edge("0000FF", 0.5),
            Left = Edge("0000FF", 0.5)
        },
        InsideHorizontalBorder = Edge(inside, 0.5),
        InsideVerticalBorder = Edge(inside, 0.5)
    };

    static BorderEdge Edge(string color, double width) => new()
    {
        IsVisible = true,
        WidthPoints = width,
        ColorHex = color
    };

    static MemoryStream BuildDocument()
    {
        static W.TableCell Cell(string text, TableCellBorders? borders)
        {
            var properties = new W.TableCellProperties();
            if (borders != null)
            {
                properties.Append(borders);
            }

            return new(properties, new Paragraph(new W.Run(new Text(text))));
        }

        var table = new Table(
            new W.TableProperties(new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })),
            new W.TableRow(
                Cell("h1", new TableCellBorders(new BottomBorder { Val = BorderValues.Single, Size = 12 })),
                Cell("h2", null)),
            new W.TableRow(
                Cell("b1", new TableCellBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 12 },
                    new LeftBorder { Val = BorderValues.Nil })),
                Cell("b2", new TableCellBorders(new TopLeftToBottomRightCellBorder { Val = BorderValues.Single, Size = 4 }))));

        var body = new Body(table, new Paragraph(new W.Run(new Text("after"))));
        var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = [with(body)];
        }

        stream.Position = 0;
        return stream;
    }
}
