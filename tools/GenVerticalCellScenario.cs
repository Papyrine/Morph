#:package DocumentFormat.OpenXml@3.3.0
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

var source = args[0];
var target = args[1];
File.Copy(source, target, overwrite: true);

using var doc = WordprocessingDocument.Open(target, isEditable: true);
var mainPart = doc.MainDocumentPart!;
var body = mainPart.Document.Body!;
foreach (var p in body.Elements<Paragraph>().ToList()) p.Remove();
foreach (var t in body.Elements<Table>().ToList()) t.Remove();

var stylesPart = mainPart.StyleDefinitionsPart ?? mainPart.AddNewPart<StyleDefinitionsPart>();
var styles = stylesPart.Styles ??= new Styles();
var docDefaults = styles.DocDefaults ??= new DocDefaults();
var rPrDefault = docDefaults.RunPropertiesDefault ??= new RunPropertiesDefault();
rPrDefault.RunPropertiesBaseStyle = new RunPropertiesBaseStyle(
    new RunFonts { Ascii = "Aptos", HighAnsi = "Aptos", ComplexScript = "Aptos" });
stylesPart.Styles.Save();

var sectPr = body.Elements<SectionProperties>().Last();
sectPr.Remove();

body.Append(new Paragraph(new Run(new Text("Quarterly results:"))));

// 2x3 table: header row has a btLr ("Quarter") cell and a horizontal ("Region") cell.
// Data rows use horizontal default cells. Row height pinned so the rotated label has
// a stable visual frame.
TableCell VerticalCell(string text, int widthTwips, TextDirectionValues dirVal)
{
    var cell = new TableCell();
    var tcPr = new TableCellProperties();
    tcPr.Append(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = widthTwips.ToString() });
    tcPr.Append(new TextDirection { Val = dirVal });
    cell.Append(tcPr);
    var p = new Paragraph(new Run(new Text(text)));
    cell.Append(p);
    return cell;
}

TableCell PlainCell(string text, int widthTwips)
{
    var cell = new TableCell();
    var tcPr = new TableCellProperties();
    tcPr.Append(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = widthTwips.ToString() });
    cell.Append(tcPr);
    cell.Append(new Paragraph(new Run(new Text(text))));
    return cell;
}

TableRow MakeRow(int heightTwips, params TableCell[] cells)
{
    var row = new TableRow();
    var trPr = new TableRowProperties();
    trPr.Append(new TableRowHeight { HeightType = HeightRuleValues.AtLeast, Val = (uint)heightTwips });
    row.Append(trPr);
    foreach (var c in cells)
    {
        row.Append(c);
    }
    return row;
}

var table = new Table();
var tblPr = new TableProperties();
tblPr.Append(new TableBorders(
    new TopBorder { Val = BorderValues.Single, Size = 4 },
    new LeftBorder { Val = BorderValues.Single, Size = 4 },
    new BottomBorder { Val = BorderValues.Single, Size = 4 },
    new RightBorder { Val = BorderValues.Single, Size = 4 },
    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }));
table.Append(tblPr);

var grid = new TableGrid();
grid.Append(new GridColumn { Width = "720" });
grid.Append(new GridColumn { Width = "3000" });
table.Append(grid);

table.Append(MakeRow(1800,
    VerticalCell("Quarter", 720, TextDirectionValues.BottomToTopLeftToRight),
    PlainCell("Region", 3000)));
table.Append(MakeRow(800,
    PlainCell("Q1", 720),
    PlainCell("North", 3000)));
table.Append(MakeRow(800,
    PlainCell("Q2", 720),
    PlainCell("South", 3000)));

body.Append(table);

body.Append(sectPr);
mainPart.Document.Save();
Console.WriteLine($"Updated {target}");
