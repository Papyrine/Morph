#:package DocumentFormat.OpenXml@3.3.0
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

var source = args[0];
var target = args[1];
File.Copy(source, target, overwrite: true);

using var doc = WordprocessingDocument.Open(target, isEditable: true);
var body = doc.MainDocumentPart!.Document.Body!;
foreach (var p in body.Elements<Paragraph>().ToList()) p.Remove();

var sectPr = body.Elements<SectionProperties>().Last();
sectPr.Remove();

// Tall table: 1 header row + 60 data rows so it spans multiple pages.
var table = new Table();
var tblPr = new TableProperties();
tblPr.Append(new TableWidth { Type = TableWidthUnitValues.Dxa, Width = "9000" });
var borders = new TableBorders(
    new TopBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
    new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
    new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
    new RightBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "000000" },
    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "000000" });
tblPr.Append(borders);
table.Append(tblPr);

var grid = new TableGrid();
grid.Append(new GridColumn { Width = "3000" });
grid.Append(new GridColumn { Width = "3000" });
grid.Append(new GridColumn { Width = "3000" });
table.Append(grid);

// Header row marked with w:tblHeader.
var headerRow = new TableRow();
var trPr = new TableRowProperties();
trPr.Append(new TableHeader());
headerRow.Append(trPr);
foreach (var label in new[] { "ID", "Name", "Notes" })
{
    var cell = new TableCell();
    cell.Append(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "3000" }));
    var p = new Paragraph();
    var rPr = new RunProperties();
    rPr.Append(new Bold());
    var r = new Run();
    r.Append(rPr);
    r.Append(new Text(label));
    p.Append(r);
    cell.Append(p);
    headerRow.Append(cell);
}
table.Append(headerRow);

// 60 data rows.
for (var i = 1; i <= 60; i++)
{
    var row = new TableRow();
    foreach (var value in new[] { i.ToString(), $"Person {i}", "Lorem ipsum dolor sit amet" })
    {
        var cell = new TableCell();
        cell.Append(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "3000" }));
        cell.Append(new Paragraph(new Run(new Text(value))));
        row.Append(cell);
    }
    table.Append(row);
}

body.Append(table);
body.Append(sectPr);

doc.MainDocumentPart.Document.Save();
Console.WriteLine($"Updated {target}");
